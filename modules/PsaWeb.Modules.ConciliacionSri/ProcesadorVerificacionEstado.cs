using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PsaWeb.Comprobantes.Compras;
using PsaWeb.Conciliacion;
using PsaWeb.Conciliacion.Data;
using PsaWeb.PeachEbills;
using PsaWeb.PeachEbills.Data;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.ConciliacionSri;

public sealed record ResumenVerificacionEmpresa(
    string Ruc, string Nombre, int Verificados, int Anulados, int ConErrores, IReadOnlyList<string> Mensajes);

public sealed record ResumenVerificacionCorrida(
    DateTimeOffset Inicio, DateTimeOffset Fin, IReadOnlyList<ResumenVerificacionEmpresa> Empresas)
{
    public int TotalVerificados => Empresas.Sum(e => e.Verificados);
    public int TotalAnulados => Empresas.Sum(e => e.Anulados);
    public int TotalConErrores => Empresas.Sum(e => e.ConErrores);
}

/// <summary>
/// Verifica contra el WS del SRI las filas <see cref="ClasificacionConciliacion.CoincidePendienteDeVerificar"/>
/// que no se verificaron hace menos de <see cref="ConciliacionOptions.VerificacionUmbralDias"/>
/// días — es lo único que puede convertir una fila "todo bien" en la salida 5
/// real (anulado). Port del mismo patrón cross-company de <c>ProcesadorRetenciones</c>,
/// pero de solo lectura: no emite nada, no escribe en Sage/Datil.
/// </summary>
public sealed class ProcesadorVerificacionEstado(
    IEmpresasActivasRepository empresasActivas,
    PeachConnStringResolver conexionesSage,
    ISageConnectionFactory sageFactory,
    ILectorComprobantesSri lectorSri,
    IVerificadorEstadoSri verificador,
    IRepositorioComprobantesSri repositorioSri,
    IOptions<ConciliacionOptions> opciones,
    ILogger<ProcesadorVerificacionEstado> logger)
{
    public async Task<ResumenVerificacionCorrida> ProcesarTodasAsync(CancellationToken cancellationToken = default)
    {
        var inicio = DateTimeOffset.Now;
        var empresas = await empresasActivas.ObtenerAsync(cancellationToken);
        using var semaforo = new SemaphoreSlim(opciones.Value.VerificacionConcurrenciaMaxima);

        var resumenes = new List<ResumenVerificacionEmpresa>();
        foreach (var empresa in empresas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            resumenes.Add(await ProcesarEmpresaAsync(empresa.Ruc, empresa.Nombre, semaforo, cancellationToken));
        }

        return new ResumenVerificacionCorrida(inicio, DateTimeOffset.Now, resumenes);
    }

    /// <summary>
    /// Para el botón "Verificar pendientes (N)", acotado a una sola empresa (la
    /// de la sesión) — envuelta en <see cref="ResumenVerificacionCorrida"/> con
    /// 1 empresa, mismo patrón que <c>ProcesadorRetenciones.ProcesarUnaAsync</c>,
    /// así el mismo <see cref="EjecucionVerificacionGate"/> sirve para ambos casos.
    /// </summary>
    public async Task<ResumenVerificacionCorrida> ProcesarUnaAsync(
        string ruc, string nombre, CancellationToken cancellationToken = default)
    {
        var inicio = DateTimeOffset.Now;
        using var semaforo = new SemaphoreSlim(opciones.Value.VerificacionConcurrenciaMaxima);
        var resumen = await ProcesarEmpresaAsync(ruc, nombre, semaforo, cancellationToken);
        return new ResumenVerificacionCorrida(inicio, DateTimeOffset.Now, [resumen]);
    }

    /// <summary>El revisor acepta una diferencia (§ítem 4 del feedback post-deploy) a su criterio.</summary>
    public Task AceptarDiferenciaAsync(
        long comprobanteId, string aceptadaPor, string? comentario, CancellationToken cancellationToken = default) =>
        repositorioSri.AceptarDiferenciaAsync(comprobanteId, aceptadaPor, comentario, cancellationToken);

    /// <summary>Deshace una aceptación marcada por error.</summary>
    public Task QuitarAceptacionAsync(long comprobanteId, CancellationToken cancellationToken = default) =>
        repositorioSri.QuitarAceptacionAsync(comprobanteId, cancellationToken);

    /// <summary>El desglose de líneas de una compra en Sage, para el popup de detalle (§ítem 2 del feedback post-deploy).</summary>
    public async Task<IReadOnlyList<LineaCompraSage>> LeerLineasSageAsync(
        string ruc, long postOrder, CancellationToken cancellationToken = default)
    {
        var cadenaSage = await conexionesSage.ResolverCadenaOdbcAsync(ruc, cancellationToken);
        await using var conexion = sageFactory.CreateConnection(cadenaSage);
        await conexion.OpenAsync(cancellationToken);
        return await LectorLineasCompraSage.LeerAsync(conexion, postOrder, cancellationToken);
    }

    /// <summary>Para el botón individual "Verificar con el SRI" de una fila puntual.</summary>
    public async Task<ResultadoVerificacionEstado> VerificarUnaAsync(
        long comprobanteId, string claveAcceso, CancellationToken cancellationToken = default)
    {
        var resultado = await verificador.VerificarAsync(claveAcceso, cancellationToken);
        // FueraDeRango no se guarda: no pisa un estado bueno con un "no se puede consultar".
        if (resultado.Estado is not (EstadoComprobanteSri.ErrorServicio or EstadoComprobanteSri.FueraDeRango))
        {
            await repositorioSri.ActualizarEstadoAsync(comprobanteId, resultado.Estado.ToString(), DateTime.UtcNow, cancellationToken);
        }

        return resultado;
    }

    /// <summary>
    /// Lee Set A + Set B y concilia — sin verificar estado. Es lo que consume
    /// la página para mostrar las 4 salidas instantáneas (§13.3); la
    /// verificación de estado es un paso aparte, bajo demanda o del worker.
    /// </summary>
    public async Task<IReadOnlyList<FilaConciliacion>> ConciliarAsync(
        string ruc, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken = default)
    {
        var setA = await lectorSri.ObtenerAsync(ruc, desde, hasta, cancellationToken);
        if (setA.Count == 0)
        {
            // Sin comprobantes del SRI para el período: nada que conciliar — ni
            // siquiera hace falta que Sage esté alcanzable para saberlo.
            return [];
        }

        var cadenaSage = await conexionesSage.ResolverCadenaOdbcAsync(ruc, cancellationToken);

        await using var conexion = sageFactory.CreateConnection(cadenaSage);
        await conexion.OpenAsync(cancellationToken);
        var setB = await LectorComprasParaConciliacion.LeerAsync(conexion, desde, hasta, cancellationToken);

        return MotorConciliacion.Conciliar(setA, setB);
    }

    private async Task<ResumenVerificacionEmpresa> ProcesarEmpresaAsync(
        string ruc, string nombre, SemaphoreSlim semaforo, CancellationToken cancellationToken)
    {
        var hasta = DateOnly.FromDateTime(DateTime.Today);
        var desde = hasta.AddDays(-opciones.Value.Worker.VentanaDias);

        IReadOnlyList<FilaConciliacion> filas;
        try
        {
            filas = await ConciliarAsync(ruc, desde, hasta, cancellationToken);
        }
        catch (Exception ex)
        {
            return Fallo(ruc, nombre, $"No se pudo conciliar: {ex.Message}");
        }

        var umbral = TimeSpan.FromDays(opciones.Value.VerificacionUmbralDias);
        var ahora = DateTime.UtcNow;
        var pendientes = SeleccionarPendientesDeVerificar(filas, umbral, ahora);

        if (pendientes.Count == 0)
        {
            return new ResumenVerificacionEmpresa(ruc, nombre, 0, 0, 0, []);
        }

        var verificados = 0;
        var anulados = 0;
        var conErrores = 0;
        var mensajes = new List<string>();

        foreach (var fila in pendientes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await semaforo.WaitAsync(cancellationToken);
            try
            {
                var resultado = await verificador.VerificarAsync(fila.ClaveAcceso!, cancellationToken);
                if (resultado.Estado == EstadoComprobanteSri.FueraDeRango)
                {
                    continue; // el WS no responde por este comprobante (fuera de rango): ni error ni verificado
                }

                if (resultado.Estado == EstadoComprobanteSri.ErrorServicio)
                {
                    conErrores++;
                    mensajes.Add($"{fila.ClaveAcceso}: {resultado.MensajeSri}");
                    continue;
                }

                await repositorioSri.ActualizarEstadoAsync(
                    fila.Sri!.Id, resultado.Estado.ToString(), ahora, cancellationToken);
                verificados++;

                if (resultado.Estado is EstadoComprobanteSri.NoAutorizado or EstadoComprobanteSri.Anulado or EstadoComprobanteSri.Otro)
                {
                    anulados++;
                    mensajes.Add($"{fila.ClaveAcceso}: NO AUTORIZADO en el SRI — contabilizado como vigente en Sage.");
                }
            }
            catch (Exception ex)
            {
                conErrores++;
                mensajes.Add($"{fila.ClaveAcceso}: error al verificar — {ex.Message}");
                logger.LogError(ex, "Verificación de estado de {Clave} ({Ruc})", fila.ClaveAcceso, ruc);
            }
            finally
            {
                semaforo.Release();
            }
        }

        return new ResumenVerificacionEmpresa(ruc, nombre, verificados, anulados, conErrores, mensajes);
    }

    private static ResumenVerificacionEmpresa Fallo(string ruc, string nombre, string mensaje) =>
        new(ruc, nombre, 0, 0, 1, [mensaje]);

    /// <summary>
    /// Candidatas a "Verificar pendientes": ya no es solo <see cref="ClasificacionConciliacion.CoincidePendienteDeVerificar"/>
    /// — las filas con diferencias (§ítem 3 del feedback post-deploy: el revisor
    /// quiere saber si un comprobante con diferencia sigue autorizado, no solo
    /// los que coinciden del todo) también se re-verifican, respetando el mismo
    /// umbral para no golpear el WS del SRI en cada corrida. Lógica pura,
    /// separada para poder testearla sin Sage/DB.
    /// </summary>
    internal static List<FilaConciliacion> SeleccionarPendientesDeVerificar(
        IReadOnlyList<FilaConciliacion> filas, TimeSpan umbral, DateTime ahora) =>
        filas
            .Where(f => f.Clasificacion is ClasificacionConciliacion.CoincidePendienteDeVerificar
                or ClasificacionConciliacion.ValoresDistintos
                or ClasificacionConciliacion.MetadataDistinta)
            .Where(f => f.Sri!.FechaVerificacionEstado is null || ahora - f.Sri.FechaVerificacionEstado.Value > umbral)
            .ToList();
}
