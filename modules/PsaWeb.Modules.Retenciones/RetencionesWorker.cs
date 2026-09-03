using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PsaWeb.Modules.Retenciones;

/// <summary>
/// Corre <see cref="ProcesadorRetenciones"/> en intervalo dentro del Host. Pasa
/// siempre por <see cref="EjecucionRetencionesGate"/>, así una corrida automática
/// nunca se solapa con «Ejecutar ahora» ni consigo misma si una tanda se alarga.
/// Deshabilitado por defecto: se prende con <c>Retenciones:Worker:Habilitado=true</c>.
/// </summary>
public sealed class RetencionesWorker : BackgroundService
{
    private static readonly TimeSpan IntervaloMinimo = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EjecucionRetencionesGate _gate;
    private readonly RetencionesOptions.WorkerOptions _opciones;
    private readonly ILogger<RetencionesWorker> _logger;

    public RetencionesWorker(
        IServiceScopeFactory scopeFactory,
        EjecucionRetencionesGate gate,
        IOptions<RetencionesOptions> opciones,
        ILogger<RetencionesWorker> logger)
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
                "Worker de retenciones DESHABILITADO (Retenciones:Worker:Habilitado=false).");
            return;
        }

        var intervalo = _opciones.Intervalo < IntervaloMinimo ? IntervaloMinimo : _opciones.Intervalo;
        _logger.LogInformation(
            "Worker de retenciones ACTIVO. Primera corrida en {Retraso}, luego cada {Intervalo}.",
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
            var procesador = scope.ServiceProvider.GetRequiredService<ProcesadorRetenciones>();

            var resultado = await _gate.EjecutarAsync(
                "worker",
                ct => procesador.ProcesarTodasAsync(_opciones.Usuario, ct),
                stoppingToken);

            if (!resultado.Ejecuto)
            {
                _logger.LogInformation("Corrida automática omitida: {Motivo}", resultado.Motivo);
                return;
            }

            var r = resultado.Resumen!;
            _logger.LogInformation(
                "Corrida automática: {Empresas} empresas · {Emitidas} {Verbo} · {Errores} con error · dryRun={DryRun}.",
                r.Empresas.Count, r.TotalEmitidas, r.DryRun ? "armadas" : "emitidas", r.TotalConErrores, r.DryRun);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado normal del Host.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "La corrida automática de retenciones falló.");
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
