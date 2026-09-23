using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PsaWeb.Comprobantes.Compras;
using PsaWeb.Comprobantes.Venta;
using PsaWeb.Conciliacion;
using PsaWeb.Conciliacion.Data;
using PsaWeb.PeachEbills;
using PsaWeb.PeachEbills.Data;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.ConciliacionSri;

public sealed record ResumenVerificacionEmpresa(
    string Ruc, string Nombre, int Verificados, int Anulados, int ConErrores, IReadOnlyList<string> Mensajes);

/// <summary>Filas de la conciliación + avisos que no impiden mostrarla (un tipo que no se pudo leer, un reporte que falta).</summary>
public sealed record ResultadoConciliacion(IReadOnlyList<FilaConciliacion> Filas, IReadOnlyList<string> Advertencias);

public sealed record ResultadoConciliacionRevisada(IReadOnlyList<FilaConRevision> Filas, IReadOnlyList<string> Advertencias);

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
    IRepositorioRevisionesConciliacion repositorioRevisiones,
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

    /// <summary>
    /// El revisor acepta un hallazgo ("Aceptada / Revisada OK") con un comentario obligatorio.
    /// Solo aplica a las 4 categorías con algo que revisar, no a "Conciliado sin verificar".
    /// </summary>
    /// <exception cref="ArgumentException">Comentario vacío.</exception>
    public Task RevisarAsync(
        string ruc, FilaConciliacion fila, string comentario, string usuario, CancellationToken cancellationToken = default)
    {
        if (fila.Clasificacion == ClasificacionConciliacion.CoincidePendienteDeVerificar)
        {
            throw new InvalidOperationException("Una fila conciliada no tiene nada que aceptar.");
        }

        return repositorioRevisiones.RegistrarAsync(
            ruc, ClaveRevision.De(fila), ClaveRevision.Huella(fila), comentario, usuario, cancellationToken);
    }

    /// <summary>Deshace una revisión marcada por error.</summary>
    public Task QuitarRevisionAsync(string ruc, FilaConciliacion fila, CancellationToken cancellationToken = default) =>
        repositorioRevisiones.QuitarAsync(ruc, ClaveRevision.De(fila), cancellationToken);

    /// <summary>La conciliación con la revisión manual de cada fila ya resuelta — lo que muestra la página.</summary>
    public async Task<ResultadoConciliacionRevisada> ConciliarConRevisionesAsync(
        string ruc, DateOnly desde, DateOnly hasta, IReadOnlySet<TipoDocumentoRecibido>? tipos = null,
        CancellationToken cancellationToken = default)
    {
        var conciliacion = await ConciliarConAdvertenciasAsync(ruc, desde, hasta, tipos, cancellationToken);
        if (conciliacion.Filas.Count == 0)
        {
            return new ResultadoConciliacionRevisada([], conciliacion.Advertencias);
        }

        var revisiones = await repositorioRevisiones.ListarAsync(ruc, cancellationToken);
        var filas = conciliacion.Filas.Select(f =>
        {
            revisiones.TryGetValue(ClaveRevision.De(f), out var revision);
            return new FilaConRevision(f, revision is null
                ? null
                : new RevisionDeFila(revision.Comentario, revision.RevisadaPor, revision.RevisadaUtc, ClaveRevision.Evaluar(f, revision)));
        }).ToList();
        return new ResultadoConciliacionRevisada(filas, conciliacion.Advertencias);
    }

    /// <summary>El desglose de líneas de una compra en Sage, para el popup de detalle (§ítem 2 del feedback post-deploy).</summary>
    public async Task<IReadOnlyList<LineaCompraSage>> LeerLineasSageAsync(
        string ruc, long postOrder, TipoDocumentoRecibido tipo = TipoDocumentoRecibido.Factura,
        CancellationToken cancellationToken = default)
    {
        var cadenaSage = await conexionesSage.ResolverCadenaOdbcAsync(ruc, cancellationToken);
        await using var conexion = sageFactory.CreateConnection(cadenaSage);
        await conexion.OpenAsync(cancellationToken);
        return await LectorLineasCompraSage.LeerAsync(conexion, postOrder, tipo, cancellationToken);
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
        string ruc, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken = default) =>
        (await ConciliarConAdvertenciasAsync(ruc, desde, hasta, null, cancellationToken)).Filas;

    /// <summary>
    /// Igual que <see cref="ConciliarAsync"/> pero acotado a los <paramref name="tipos"/> elegidos (null = los
    /// tres: facturas, notas de crédito y retenciones) y devolviendo advertencias en vez de fallar cuando una
    /// lectura opcional de Sage no anda (p. ej. la de retenciones, todavía sin validar contra datos reales).
    /// </summary>
    public async Task<ResultadoConciliacion> ConciliarConAdvertenciasAsync(
        string ruc, DateOnly desde, DateOnly hasta, IReadOnlySet<TipoDocumentoRecibido>? tipos,
        CancellationToken cancellationToken = default)
    {
        var incluidos = tipos is null
            ? new HashSet<TipoDocumentoRecibido> { TipoDocumentoRecibido.Factura, TipoDocumentoRecibido.NotaCredito, TipoDocumentoRecibido.Retencion }
            : new HashSet<TipoDocumentoRecibido>(tipos);
        var advertencias = new List<string>();

        // Retenciones: el SRI da N días para emitirlas y las fechas del SRI y de Sage pueden distar hasta N días,
        // así que se lee una ventana de ±N alrededor del período para encontrar a la pareja (después se recorta).
        var plazo = Math.Max(opciones.Value.RetencionPlazoDias, 0);
        var ampliar = incluidos.Contains(TipoDocumentoRecibido.Retencion) && plazo > 0;
        var desdeAmplio = ampliar ? desde.AddDays(-plazo) : desde;
        var hastaAmplio = ampliar ? hasta.AddDays(plazo) : hasta;

        var setATodo = await lectorSri.ObtenerAsync(ruc, desdeAmplio, hastaAmplio, cancellationToken);
        if (setATodo.Count == 0)
        {
            // Sin comprobantes del SRI para el período: nada que conciliar — ni
            // siquiera hace falta que Sage esté alcanzable para saberlo.
            return new ResultadoConciliacion([], advertencias);
        }

        var cadenaSage = await conexionesSage.ResolverCadenaOdbcAsync(ruc, cancellationToken);

        await using var conexion = sageFactory.CreateConnection(cadenaSage);
        await conexion.OpenAsync(cancellationToken);

        var setB = new List<CompraSage>();
        if (incluidos.Contains(TipoDocumentoRecibido.Factura) || incluidos.Contains(TipoDocumentoRecibido.NotaCredito))
        {
            var compras = await LectorComprasParaConciliacion.LeerAsync(conexion, desde, hasta, cancellationToken);
            setB.AddRange(compras.Where(c => incluidos.Contains(c.Tipo)));
        }

        if (incluidos.Contains(TipoDocumentoRecibido.Retencion))
        {
            try
            {
                setB.AddRange(await LectorRetencionesRecibidasParaConciliacion.LeerAsync(conexion, desdeAmplio, hastaAmplio, cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Sin la parte de Sage no se puede conciliar el tipo: se saca también del lado del SRI para no
                // mostrar todas las retenciones como "Solo en SRI" por un error de lectura.
                incluidos.Remove(TipoDocumentoRecibido.Retencion);
                advertencias.Add($"No se pudieron leer las retenciones recibidas de Sage, así que no se concilian: {ex.Message}");
                logger.LogError(ex, "Lectura de retenciones recibidas de Sage ({Ruc})", ruc);
            }
        }

        // Facturas y notas de crédito se quedan en el período pedido; las retenciones conservan la ventana ampliada.
        var setA = setATodo
            .Where(c => c.Tipo == TipoDocumentoRecibido.Retencion
                ? incluidos.Contains(c.Tipo)
                : (c.Tipo == TipoDocumentoRecibido.Otro || incluidos.Contains(c.Tipo)) && c.FechaEmision >= desde && c.FechaEmision <= hasta)
            .ToList();

        // Un tipo elegido sin ningún comprobante del SRI cargado no es una diferencia: es un reporte que falta subir.
        foreach (var tipo in incluidos.OrderBy(t => t))
        {
            var enSage = setB.Count(c => c.Tipo == tipo);
            if (enSage > 0 && !setA.Any(c => c.Tipo == tipo))
            {
                advertencias.Add(
                    $"Hay {enSage} {TiposDocumentoRecibido.Etiqueta(tipo).ToLowerInvariant()} en Sage y ninguna del SRI cargada para este período: " +
                    "aparecen como «Solo en Sage». Subí el reporte de ese tipo desde el portal (campo «Tipo de documento») o desmarcalo.");
            }
        }

        var filas = MotorConciliacion.Conciliar(
            setA, setB, MotorConciliacion.ToleranciaMontosPorDefecto, DateOnly.FromDateTime(DateTime.Today), plazo);
        return new ResultadoConciliacion(MotorConciliacion.RecortarAlPeriodo(filas, desde, hasta), advertencias);
    }

    private async Task<ResumenVerificacionEmpresa> ProcesarEmpresaAsync(
        string ruc, string nombre, SemaphoreSlim semaforo, CancellationToken cancellationToken)
    {
        var hasta = DateOnly.FromDateTime(DateTime.Today);
        // El WS del SRI solo responde por el mes en curso + el anterior: pedir más solo generaba errores.
        var desde = DateOnly.FromDateTime(
            new[] { hasta.AddDays(-opciones.Value.Worker.VentanaDias).ToDateTime(TimeOnly.MinValue), RangoConsultaSri.Inicio(DateTime.Today) }.Max());

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

        return await VerificarAsync(ruc, nombre, pendientes, semaforo, null, cancellationToken);
    }

    /// <summary>
    /// Las filas que hoy tiene sentido verificar contra el SRI (lo que cuenta el botón "Verificar pendientes"):
    /// con comprobante del SRI, sin verificar o con la verificación vencida, y dentro del rango que el WS responde.
    /// </summary>
    public IReadOnlyList<FilaConciliacion> CandidatasAVerificar(IEnumerable<FilaConciliacion> filas) =>
        SeleccionarPendientesDeVerificar(filas.ToList(), TimeSpan.FromDays(opciones.Value.VerificacionUmbralDias), DateTime.UtcNow);

    /// <summary>
    /// Verifica exactamente las <paramref name="candidatas"/> (las que muestra la pantalla), informando el avance
    /// (cuántas van) — mismo <see cref="ResumenVerificacionCorrida"/> de 1 empresa que <see cref="ProcesarUnaAsync"/>
    /// para poder pasar por el mismo <see cref="EjecucionVerificacionGate"/>.
    /// </summary>
    public async Task<ResumenVerificacionCorrida> VerificarFilasAsync(
        string ruc, string nombre, IReadOnlyList<FilaConciliacion> candidatas, IProgress<int>? avance = null,
        CancellationToken cancellationToken = default)
    {
        var inicio = DateTimeOffset.Now;
        using var semaforo = new SemaphoreSlim(opciones.Value.VerificacionConcurrenciaMaxima);
        var resumen = await VerificarAsync(ruc, nombre, candidatas, semaforo, avance, cancellationToken);
        return new ResumenVerificacionCorrida(inicio, DateTimeOffset.Now, [resumen]);
    }

    // Las llamadas al WS del SRI corren en paralelo (acotadas por el semáforo); las escrituras a la base van de a una
    // porque el DbContext no es seguro entre hilos.
    private async Task<ResumenVerificacionEmpresa> VerificarAsync(
        string ruc, string nombre, IReadOnlyList<FilaConciliacion> pendientes, SemaphoreSlim semaforo,
        IProgress<int>? avance, CancellationToken cancellationToken)
    {
        if (pendientes.Count == 0)
        {
            return new ResumenVerificacionEmpresa(ruc, nombre, 0, 0, 0, []);
        }

        var ahora = DateTime.UtcNow;
        var verificados = 0;
        var anulados = 0;
        var conErrores = 0;
        var hechos = 0;
        var mensajes = new List<string>();
        var candadoMensajes = new object();
        using var candadoDb = new SemaphoreSlim(1, 1);

        void Mensaje(string texto)
        {
            lock (candadoMensajes)
            {
                mensajes.Add(texto);
            }
        }

        await Task.WhenAll(pendientes.Select(async fila =>
        {
            await semaforo.WaitAsync(cancellationToken);
            try
            {
                var resultado = await verificador.VerificarAsync(fila.ClaveAcceso!, cancellationToken);
                if (resultado.Estado == EstadoComprobanteSri.FueraDeRango)
                {
                    return; // el WS no responde por este comprobante (fuera de rango): ni error ni verificado
                }

                if (resultado.Estado == EstadoComprobanteSri.ErrorServicio)
                {
                    Interlocked.Increment(ref conErrores);
                    Mensaje($"{fila.ClaveAcceso}: {resultado.MensajeSri}");
                    return;
                }

                await candadoDb.WaitAsync(cancellationToken);
                try
                {
                    await repositorioSri.ActualizarEstadoAsync(
                        fila.Sri!.Id, resultado.Estado.ToString(), ahora, cancellationToken);
                }
                finally
                {
                    candadoDb.Release();
                }

                Interlocked.Increment(ref verificados);
                if (resultado.Estado is EstadoComprobanteSri.NoAutorizado or EstadoComprobanteSri.Anulado or EstadoComprobanteSri.Otro)
                {
                    Interlocked.Increment(ref anulados);
                    Mensaje($"{fila.ClaveAcceso}: NO AUTORIZADO en el SRI — contabilizado como vigente en Sage.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Interlocked.Increment(ref conErrores);
                Mensaje($"{fila.ClaveAcceso}: error al verificar — {ex.Message}");
                logger.LogError(ex, "Verificación de estado de {Clave} ({Ruc})", fila.ClaveAcceso, ruc);
            }
            finally
            {
                semaforo.Release();
                avance?.Report(Interlocked.Increment(ref hechos));
            }
        }));

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
            .Where(f => f.Sri is not null)
            .Where(f => f.Sri!.FechaVerificacionEstado is null || ahora - f.Sri.FechaVerificacionEstado.Value > umbral)
            // El WS solo responde por el mes en curso y el anterior: fuera de eso no hay nada que verificar y contarlas
            // como pendientes dejaría el botón con un número que nunca baja.
            .Where(f => RangoConsultaSri.Contiene(f.Sri!.FechaEmision.ToDateTime(TimeOnly.MinValue), ahora.ToLocalTime()))
            .ToList();
}
