using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PsaWeb.Conciliacion;
using PsaWeb.Conciliacion.Data;

namespace PsaWeb.Modules.ComprobantesElectronicos.Estado;

public enum ResolucionAnulacion { SigueAbierta, Anulada, SinEfecto }

/// <summary>
/// Reglas del seguimiento de una solicitud de anulación (puras, sin base de datos).
/// El SRI no informa el estado intermedio «pendiente de aceptación» (no hay un caso real que lo
/// muestre), así que el seguimiento se apoya en la <em>fecha de la solicitud</em> que guarda la app.
/// </summary>
public static class MaquinaAnulacion
{
    /// <summary>Retenciones y notas de crédito: si el receptor no acepta, a los 5 días vuelve a vigente.</summary>
    public const int DiasParaSinEfecto = 5;

    /// <summary>Facturas y liquidaciones se anulan directo; retenciones y NC requieren que el receptor acepte.</summary>
    public static bool RequiereAceptacionDelReceptor(TipoComprobante tipo) =>
        tipo is TipoComprobante.Retencion or TipoComprobante.NotaCredito;

    public static ResolucionAnulacion Evaluar(
        TipoComprobante tipo, DateTime fechaSolicitudUtc, string? estadoSri, DateTime ahoraUtc)
    {
        if (estadoSri == nameof(EstadoComprobanteSri.Anulado))
        {
            return ResolucionAnulacion.Anulada;
        }

        if (RequiereAceptacionDelReceptor(tipo)
            && estadoSri == nameof(EstadoComprobanteSri.Autorizado)
            && DiasAbierta(fechaSolicitudUtc, ahoraUtc) >= DiasParaSinEfecto)
        {
            return ResolucionAnulacion.SinEfecto;
        }

        return ResolucionAnulacion.SigueAbierta;
    }

    public static int DiasAbierta(DateTime fechaSolicitudUtc, DateTime ahoraUtc) =>
        Math.Max(0, (int)Math.Floor((ahoraUtc - fechaSolicitudUtc).TotalDays));
}

/// <summary>Discrepancias entre lo que dice el SRI y lo que dice PeachEBills (<c>IsValid</c>).</summary>
public static class Discrepancias
{
    public static string? Evaluar(string? estadoSri, bool validoEnPeachEbills)
    {
        if (validoEnPeachEbills && EstadoSriPresentacion.NoVigente(estadoSri))
        {
            return "Anulado en el SRI pero vigente en PeachEBills";
        }
        if (!validoEnPeachEbills && estadoSri == nameof(EstadoComprobanteSri.Autorizado))
        {
            return "Anulado en PeachEBills pero vigente en el SRI";
        }
        return null;
    }
}

public sealed record ResumenSeguimiento(int Abiertas, int Anuladas, int SinEfecto, int SinVerificar);

/// <summary>
/// Guarda las solicitudes de anulación y las sigue hasta que el SRI las refleje
/// (tabla <c>SolicitudesAnulacion</c> de <c>PsaWebPlataforma</c>). La anulación real la hace una
/// persona en el portal del SRI; este servicio solo la registra y la rastrea.
/// </summary>
public sealed class ServicioAnulaciones
{
    private readonly DbContextOptions<ConciliacionDbContext>? _opcionesDb;
    private readonly ServicioEstadoSri _estado;
    private readonly ILogger<ServicioAnulaciones> _logger;

    public ServicioAnulaciones(IServiceProvider sp, ServicioEstadoSri estado, ILogger<ServicioAnulaciones> logger)
    {
        _opcionesDb = sp.GetService<DbContextOptions<ConciliacionDbContext>>();
        _estado = estado;
        _logger = logger;
    }

    public bool Disponible => _opcionesDb is not null;

    /// <summary>
    /// Registra la solicitud (idempotente: si ya hay una abierta la devuelve). Los comprobantes de
    /// pruebas no existen en el SRI real y no se siguen: devuelve null.
    /// </summary>
    public async Task<SolicitudAnulacionComprobante?> RegistrarAsync(
        ComprobanteAVerificar c, string numero, string usuario, CancellationToken ct = default)
    {
        if (!Disponible || c.Ambiente != 2) return null;

        var codDoc = Tipos.De(c.Tipo).CodDoc;
        await using var db = new ConciliacionDbContext(_opcionesDb!);

        var abierta = await db.SolicitudesAnulacion.FirstOrDefaultAsync(
            s => s.Ruc == c.Ruc && s.CodDoc == codDoc && s.RefId == c.RefId && s.Estado == EstadoAnulacion.Solicitada, ct);
        if (abierta is not null) return abierta;

        var nueva = new SolicitudAnulacionComprobante
        {
            Ruc = c.Ruc,
            CodDoc = codDoc,
            RefId = c.RefId,
            Numero = numero.Length > 40 ? numero[..40] : numero,
            DatilId = c.DatilId,
            FechaEmision = DateOnly.FromDateTime(c.FechaEmision),
            Ambiente = c.Ambiente,
            SolicitadaPor = usuario.Length > 100 ? usuario[..100] : usuario,
            FechaSolicitud = DateTime.UtcNow,
            Estado = EstadoAnulacion.Solicitada,
        };
        db.SolicitudesAnulacion.Add(nueva);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dos solicitudes simultáneas del mismo comprobante (índice único filtrado): quedarse con la que ganó.
            db.Entry(nueva).State = EntityState.Detached;
            return await db.SolicitudesAnulacion.AsNoTracking().FirstOrDefaultAsync(
                s => s.Ruc == c.Ruc && s.CodDoc == codDoc && s.RefId == c.RefId && s.Estado == EstadoAnulacion.Solicitada, ct);
        }
        return nueva;
    }

    /// <summary>Solicitudes abiertas de una lista de comprobantes de un tipo, por (RUC, id).</summary>
    public async Task<IReadOnlyDictionary<(string Ruc, int RefId), SolicitudAnulacionComprobante>> ObtenerAbiertasAsync(
        TipoComprobante tipo, IReadOnlyCollection<(string Ruc, int RefId)> items, CancellationToken ct = default)
    {
        var resultado = new Dictionary<(string, int), SolicitudAnulacionComprobante>();
        if (!Disponible || items.Count == 0) return resultado;

        var codDoc = Tipos.De(tipo).CodDoc;
        var rucs = items.Select(i => i.Ruc).Distinct().ToArray();
        var ids = items.Select(i => i.RefId).Distinct().ToArray();

        await using var db = new ConciliacionDbContext(_opcionesDb!);
        foreach (var tanda in ids.Chunk(1000))
        {
            var filas = await db.SolicitudesAnulacion.AsNoTracking()
                .Where(s => s.CodDoc == codDoc && s.Estado == EstadoAnulacion.Solicitada
                            && EF.Constant(rucs).Contains(s.Ruc) && EF.Constant(tanda).Contains(s.RefId))
                .ToListAsync(ct);
            foreach (var f in filas) resultado[(f.Ruc, f.RefId)] = f;
        }
        return resultado;
    }

    /// <summary>Retira una solicitud abierta (solo quien puede autorizar anulaciones). false si ya no estaba abierta.</summary>
    public async Task<bool> CancelarAsync(long id, string usuario, CancellationToken ct = default)
    {
        if (!Disponible) return false;
        await using var db = new ConciliacionDbContext(_opcionesDb!);
        var s = await db.SolicitudesAnulacion.FirstOrDefaultAsync(x => x.Id == id && x.Estado == EstadoAnulacion.Solicitada, ct);
        if (s is null) return false;

        s.Estado = EstadoAnulacion.Cancelada;
        s.FechaResolucion = DateTime.UtcNow;
        s.ResueltaPor = usuario.Length > 100 ? usuario[..100] : usuario;
        s.Detalle = "Solicitud retirada.";
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Sigue todas las solicitudes abiertas: verifica cada comprobante (SRI, o Datil fuera del rango del SRI)
    /// y aplica <see cref="MaquinaAnulacion"/>. Lo corre el worker (y se puede lanzar a mano).
    /// </summary>
    public async Task<ResumenSeguimiento> ActualizarAbiertasAsync(CancellationToken ct = default)
    {
        if (!Disponible) return new ResumenSeguimiento(0, 0, 0, 0);

        await using var db = new ConciliacionDbContext(_opcionesDb!);
        var abiertas = await db.SolicitudesAnulacion.Where(s => s.Estado == EstadoAnulacion.Solicitada).ToListAsync(ct);

        int anuladas = 0, sinEfecto = 0, sinVerificar = 0;
        foreach (var s in abiertas)
        {
            ct.ThrowIfCancellationRequested();
            var tipo = Tipos.PorCodDoc(s.CodDoc).Tipo;
            var r = await _estado.VerificarAsync(
                new ComprobanteAVerificar(tipo, s.Ruc, s.RefId, s.FechaEmision.ToDateTime(TimeOnly.MinValue), s.DatilId, s.Ambiente), ct);

            if (r.Estado is null)
            {
                sinVerificar++;
                s.Detalle = r.Detalle is { Length: > 500 } ? r.Detalle[..500] : r.Detalle;
                continue;
            }

            var ahora = DateTime.UtcNow;
            s.UltimoEstadoSri = r.Estado;
            s.FechaUltimaVerificacion = ahora;
            switch (MaquinaAnulacion.Evaluar(tipo, s.FechaSolicitud, r.Estado, ahora))
            {
                case ResolucionAnulacion.Anulada:
                    Cerrar(s, EstadoAnulacion.Anulada, ahora, "El SRI confirmó ANULADO.");
                    anuladas++;
                    break;
                case ResolucionAnulacion.SinEfecto:
                    Cerrar(s, EstadoAnulacion.SinEfecto, ahora,
                        $"Pasaron {MaquinaAnulacion.DiasParaSinEfecto} días y el SRI sigue mostrándolo autorizado.");
                    sinEfecto++;
                    break;
            }
        }

        await db.SaveChangesAsync(ct);
        var resumen = new ResumenSeguimiento(abiertas.Count, anuladas, sinEfecto, sinVerificar);
        _logger.LogInformation(
            "Seguimiento de anulaciones: {Abiertas} abiertas · {Anuladas} anuladas · {SinEfecto} sin efecto · {SinVerificar} sin poder verificar.",
            resumen.Abiertas, resumen.Anuladas, resumen.SinEfecto, resumen.SinVerificar);
        return resumen;
    }

    private static void Cerrar(SolicitudAnulacionComprobante s, string estado, DateTime ahora, string detalle)
    {
        s.Estado = estado;
        s.FechaResolucion = ahora;
        s.ResueltaPor = "worker";
        s.Detalle = detalle;
    }
}
