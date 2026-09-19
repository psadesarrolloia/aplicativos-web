namespace PsaWeb.Modules.ComprobantesElectronicos.Retenciones;

/// <summary>Resultado de intentar una corrida a través del <see cref="EjecucionRetencionesGate"/>.</summary>
public sealed record ResultadoEjecucion(bool Ejecuto, ResumenCorrida? Resumen, string? Motivo);

/// <summary>Instantánea del estado del gate para mostrar en la UI.</summary>
public sealed record EstadoEjecucion(
    bool EnCurso,
    string? OrigenActual,
    DateTimeOffset? InicioActual,
    DateTimeOffset? FinUltima,
    string? OrigenUltima,
    ResumenCorrida? ResumenUltima,
    string? ErrorUltima);

/// <summary>
/// Candado de un solo cupo (single-flight) compartido por el botón «Ejecutar
/// ahora» de la página y el <c>RetencionesWorker</c>. Garantiza que nunca corran
/// dos generaciones de retenciones a la vez. Singleton.
/// </summary>
public sealed class EjecucionRetencionesGate
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    private volatile bool _enCurso;
    private string? _origenActual;
    private DateTimeOffset? _inicioActual;
    private DateTimeOffset? _finUltima;
    private string? _origenUltima;
    private ResumenCorrida? _resumenUltima;
    private string? _errorUltima;

    public EstadoEjecucion Estado => new(
        _enCurso, _origenActual, _inicioActual, _finUltima, _origenUltima, _resumenUltima, _errorUltima);

    /// <summary>
    /// Igual que <see cref="EjecutarAsync"/> pero para trabajos de otro tipo de
    /// resultado (generar UNA retención o un lote acotado desde la interfaz
    /// unificada). Comparte el mismo candado: nunca corren a la vez que el
    /// worker ni entre sí. Devuelve <c>Ejecuto == false</c> sin esperar si está ocupado.
    /// </summary>
    public async Task<(bool Ejecuto, T? Valor, string? Motivo)> IntentarAsync<T>(
        string origen,
        Func<CancellationToken, Task<T>> trabajo,
        CancellationToken cancellationToken = default)
    {
        if (!await _lock.WaitAsync(0, cancellationToken))
        {
            return (false, default, "Ya hay una corrida de retenciones en curso.");
        }

        _enCurso = true;
        _origenActual = origen;
        _inicioActual = DateTimeOffset.Now;
        try
        {
            return (true, await trabajo(cancellationToken), null);
        }
        finally
        {
            _enCurso = false;
            _origenActual = null;
            _inicioActual = null;
            _lock.Release();
        }
    }

    /// <summary>
    /// Corre <paramref name="corrida"/> si el candado está libre. Si ya hay una
    /// corrida en curso devuelve <c>Ejecuto == false</c> sin esperar.
    /// </summary>
    public async Task<ResultadoEjecucion> EjecutarAsync(
        string origen,
        Func<CancellationToken, Task<ResumenCorrida>> corrida,
        CancellationToken cancellationToken = default)
    {
        if (!await _lock.WaitAsync(0, cancellationToken))
        {
            return new ResultadoEjecucion(false, null, "Ya hay una corrida en curso.");
        }

        _enCurso = true;
        _origenActual = origen;
        _inicioActual = DateTimeOffset.Now;
        try
        {
            var resumen = await corrida(cancellationToken);
            _resumenUltima = resumen;
            _errorUltima = null;
            _origenUltima = origen;
            _finUltima = DateTimeOffset.Now;
            return new ResultadoEjecucion(true, resumen, null);
        }
        catch (Exception ex)
        {
            _resumenUltima = null;
            _errorUltima = ex.Message;
            _origenUltima = origen;
            _finUltima = DateTimeOffset.Now;
            throw;
        }
        finally
        {
            _enCurso = false;
            _origenActual = null;
            _inicioActual = null;
            _lock.Release();
        }
    }
}
