using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PsaWeb.Conciliacion;

namespace PsaWeb.Modules.ConciliacionSri;

/// <summary>
/// Corre <see cref="ProcesadorVerificacionEstado"/> en intervalo, cross-company,
/// dentro del Host — es la respuesta a que un botón manual corre riesgo real
/// de omitirse (decidido con el usuario, §13.4/§10 decisión #8). A diferencia
/// de <c>RetencionesWorker</c>, arranca <c>Habilitado=true</c> por defecto: no
/// emite nada ni escribe en Sage/Datil, solo lee el WS público del SRI y
/// actualiza estado — el peor caso es que falle y reintente.
/// </summary>
public sealed class VerificacionEstadoSriWorker : BackgroundService
{
    private static readonly TimeSpan IntervaloMinimo = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EjecucionVerificacionGate _gate;
    private readonly ConciliacionOptions.WorkerOptions _opciones;
    private readonly ILogger<VerificacionEstadoSriWorker> _logger;

    public VerificacionEstadoSriWorker(
        IServiceScopeFactory scopeFactory,
        EjecucionVerificacionGate gate,
        IOptions<ConciliacionOptions> opciones,
        ILogger<VerificacionEstadoSriWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _gate = gate;
        _opciones = opciones.Value.Worker;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opciones.Habilitado)
        {
            _logger.LogInformation(
                "Worker de verificación de estado SRI DESHABILITADO (Conciliacion:Worker:Habilitado=false).");
            return;
        }

        var intervalo = _opciones.Intervalo < IntervaloMinimo ? IntervaloMinimo : _opciones.Intervalo;
        _logger.LogInformation(
            "Worker de verificación de estado SRI ACTIVO. Primera corrida en {Retraso}, luego cada {Intervalo}.",
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

    private async Task CorrerUnaVezAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var procesador = scope.ServiceProvider.GetRequiredService<ProcesadorVerificacionEstado>();

            var resultado = await _gate.EjecutarAsync(ct => procesador.ProcesarTodasAsync(ct), stoppingToken);

            if (!resultado.Ejecuto)
            {
                _logger.LogInformation("Corrida automática de verificación omitida: {Motivo}", resultado.Motivo);
                return;
            }

            var r = resultado.Resumen!;
            _logger.LogInformation(
                "Corrida automática de verificación: {Empresas} empresas · {Verificados} verificados · {Anulados} anulados · {Errores} con error.",
                r.Empresas.Count, r.TotalVerificados, r.TotalAnulados, r.TotalConErrores);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado normal del Host.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "La corrida automática de verificación de estado SRI falló.");
        }
    }

    private static async Task<bool> EsperarSiguienteAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
