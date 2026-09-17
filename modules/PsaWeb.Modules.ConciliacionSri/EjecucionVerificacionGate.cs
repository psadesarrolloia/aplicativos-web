namespace PsaWeb.Modules.ConciliacionSri;

public sealed record ResultadoEjecucionVerificacion(bool Ejecuto, ResumenVerificacionCorrida? Resumen, string? Motivo);

/// <summary>
/// Candado de un solo cupo (single-flight) compartido por el botón "Verificar
/// pendientes" de la página y el <see cref="VerificacionEstadoSriWorker"/> —
/// mismo patrón que <c>EjecucionRetencionesGate</c>. Evita que ambos verifiquen
/// las mismas filas al mismo tiempo (no sería grave — es solo lectura del WS y
/// una actualización de estado idempotente — pero evita trabajo duplicado).
/// </summary>
public sealed class EjecucionVerificacionGate
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile bool _enCurso;

    public bool EnCurso => _enCurso;

    public async Task<ResultadoEjecucionVerificacion> EjecutarAsync(
        Func<CancellationToken, Task<ResumenVerificacionCorrida>> corrida, CancellationToken cancellationToken = default)
    {
        if (!await _lock.WaitAsync(0, cancellationToken))
        {
            return new ResultadoEjecucionVerificacion(false, null, "Ya hay una verificación en curso.");
        }

        _enCurso = true;
        try
        {
            var resumen = await corrida(cancellationToken);
            return new ResultadoEjecucionVerificacion(true, resumen, null);
        }
        finally
        {
            _enCurso = false;
            _lock.Release();
        }
    }
}
