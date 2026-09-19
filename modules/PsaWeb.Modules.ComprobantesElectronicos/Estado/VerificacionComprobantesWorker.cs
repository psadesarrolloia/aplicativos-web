using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PsaWeb.Modules.ComprobantesElectronicos.Estado;

public sealed class VerificacionComprobantesOptions
{
    public const string SectionName = "ComprobantesElectronicos:Verificacion";

    /// <summary>
    /// Arranca en <c>true</c> (como el de Conciliación): no emite ni escribe en Sage/Datil/PeachEBills;
    /// solo lee el WS público del SRI y Datil y guarda el estado en su tabla propia.
    /// </summary>
    public bool Habilitado { get; set; } = true;

    /// <summary>Tiempo entre corridas. Por defecto 8 horas (las anulaciones abiertas se sondean cada corrida).</summary>
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Espera antes de la primera corrida (deja arrancar el Host).</summary>
    public TimeSpan RetrasoInicial { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Cuántos comprobantes del histórico (fuera del rango del SRI, hasta 2 años, nunca verificados)
    /// se completan por corrida vía Datil. El histórico se cubre de a poco sin martillar el API.
    /// </summary>
    public int HistoricoPorCorrida { get; set; } = 300;
}

/// <summary>
/// Cada corrida: (1) sigue las solicitudes de anulación abiertas, (2) verifica el estado en el SRI de lo
/// emitido en el rango del SRI (y completa el histórico de a poco). Cubre los 4 tipos de comprobante.
/// </summary>
public sealed class VerificacionComprobantesWorker : BackgroundService
{
    private static readonly TimeSpan IntervaloMinimo = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VerificacionComprobantesOptions _opciones;
    private readonly ILogger<VerificacionComprobantesWorker> _logger;

    public VerificacionComprobantesWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<VerificacionComprobantesOptions> opciones,
        ILogger<VerificacionComprobantesWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _opciones = opciones.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opciones.Habilitado)
        {
            _logger.LogInformation(
                "Worker de verificación de comprobantes emitidos DESHABILITADO ({Seccion}:Habilitado=false).",
                VerificacionComprobantesOptions.SectionName);
            return;
        }

        var intervalo = _opciones.Intervalo < IntervaloMinimo ? IntervaloMinimo : _opciones.Intervalo;
        _logger.LogInformation(
            "Worker de verificación de comprobantes emitidos ACTIVO. Primera corrida en {Retraso}, luego cada {Intervalo}.",
            _opciones.RetrasoInicial, intervalo);

        try
        {
            await Task.Delay(_opciones.RetrasoInicial, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(intervalo);
        do
        {
            await CorrerUnaVezAsync(stoppingToken);
        }
        while (await EsperarSiguienteAsync(timer, stoppingToken));
    }

    private async Task CorrerUnaVezAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var estado = scope.ServiceProvider.GetRequiredService<ServicioEstadoSri>();
            if (!estado.Disponible)
            {
                _logger.LogInformation("Corrida de verificación omitida: Conciliación SRI no está registrada en este Host.");
                return;
            }

            // 1) Anulaciones abiertas primero: son las que tienen plazo (5 días).
            var anulaciones = scope.ServiceProvider.GetRequiredService<ServicioAnulaciones>();
            await anulaciones.ActualizarAbiertasAsync(ct);

            // 2) Estado en el SRI de lo emitido.
            var r = await estado.CorrerVerificacionAsync(_opciones.HistoricoPorCorrida, ct);
            _logger.LogInformation(
                "Corrida de verificación de comprobantes: {Candidatos} candidatos · {Verificados} verificados · {Anulados} anulados · {NoVerificables} no verificables · {Errores} con error.",
                r.Candidatos, r.Verificados, r.Anulados, r.NoVerificables, r.ConErrores);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló la corrida de verificación de comprobantes emitidos; se reintenta en la próxima.");
        }
    }

    private static async Task<bool> EsperarSiguienteAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
