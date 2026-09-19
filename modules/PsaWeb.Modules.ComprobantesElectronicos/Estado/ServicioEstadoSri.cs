using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PsaWeb.Conciliacion;
using PsaWeb.Conciliacion.Data;
using PsaWeb.Datil;
using PsaWeb.Modules.ComprobantesElectronicos.Data;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.ComprobantesElectronicos.Estado;

/// <summary>Un comprobante emitido por nosotros, listo para verificar su estado en el SRI.</summary>
public sealed record ComprobanteAVerificar(
    TipoComprobante Tipo, string Ruc, int RefId, DateTime FechaEmision, string? DatilId, short Ambiente);

/// <summary>Estado guardado (o recién verificado) de un comprobante.</summary>
public sealed record EstadoSri(
    string? Estado, string? Fuente, string? Detalle, DateTime? FechaVerificacion, string? ClaveAcceso, bool ConError = false)
{
    public static readonly EstadoSri SinVerificar = new(null, null, null, null, null);
}

public sealed record ResumenVerificacionMasiva(
    int Candidatos, int Verificados, int Anulados, int NoVerificables, int ConErrores, IReadOnlyList<string> Mensajes);

/// <summary>
/// Verifica en el SRI (y, fuera de su rango, en Datil) si los comprobantes que emitimos siguen
/// vigentes, y guarda el resultado en <c>PsaWebPlataforma</c> (tabla propia; no toca PeachEBills).
/// Reglas medidas el 2026-09-19:
/// <list type="bullet">
/// <item>El WS del SRI solo responde por el mes en curso y el mes anterior.</item>
/// <item>Datil responde por todo el alcance de 2 años (vigencia de la contabilidad en Sage).</item>
/// <item>La clave de acceso sale de <c>DatilRequests</c>; si falta, de Datil.</item>
/// </list>
/// Depende de que Conciliación SRI esté registrada (misma BD y mismo cliente del WS): si no,
/// <see cref="Disponible"/> es false y la interfaz oculta la columna.
/// </summary>
public sealed class ServicioEstadoSri
{
    /// <summary>Alcance temporal de la verificación (decisión 2026-09-19): 2 años.</summary>
    public const int AlcanceAnios = 2;

    private static readonly SemaphoreSlim MasivaGate = new(1, 1);

    private readonly DbContextOptions<ConciliacionDbContext>? _opcionesDb;
    private readonly IVerificadorEstadoSri? _verificadorSri;
    private readonly ConciliacionOptions _opciones;
    private readonly IDbContextFactory<PeachEbillsContext> _peach;
    private readonly IDatilClient _datil;
    private readonly EmisorLookup _emisores;
    private readonly ILogger<ServicioEstadoSri> _logger;

    public ServicioEstadoSri(
        IServiceProvider sp,
        IDbContextFactory<PeachEbillsContext> peach,
        IDatilClient datil,
        EmisorLookup emisores,
        ILogger<ServicioEstadoSri> logger)
    {
        _opcionesDb = sp.GetService<DbContextOptions<ConciliacionDbContext>>();
        _verificadorSri = sp.GetService<IVerificadorEstadoSri>();
        _opciones = sp.GetService<IOptions<ConciliacionOptions>>()?.Value ?? new ConciliacionOptions();
        _peach = peach;
        _datil = datil;
        _emisores = emisores;
        _logger = logger;
    }

    public bool Disponible => _opcionesDb is not null && _verificadorSri is not null;

    // ---------------------------------------------------------------- reglas puras

    /// <summary>Primer día del mes anterior: desde ahí el WS del SRI acepta consultas.</summary>
    public static DateTime InicioRangoSri(DateTime hoy) => new DateTime(hoy.Year, hoy.Month, 1).AddMonths(-1);

    public static bool EnRangoSri(DateTime fechaEmision, DateTime hoy) => fechaEmision.Date >= InicioRangoSri(hoy);

    public static bool EnAlcance(DateTime fechaEmision, DateTime hoy) =>
        fechaEmision.Date >= hoy.Date.AddYears(-AlcanceAnios);

    /// <summary>Extrae <c>clave_acceso</c> (49 dígitos) de un JSON de Datil; null si no hay una válida.</summary>
    public static string? ClaveDesdeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("clave_acceso", out var el)
                && el.ValueKind == JsonValueKind.String)
            {
                var clave = el.GetString();
                if (clave is { Length: 49 } && clave.All(char.IsDigit)) return clave;
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    /// <summary>Traduce el campo <c>estado</c> de Datil al mismo vocabulario del SRI.</summary>
    public static EstadoComprobanteSri EstadoDesdeDatil(string? estado) => estado?.Trim().ToUpperInvariant() switch
    {
        null or "" => EstadoComprobanteSri.ErrorServicio,
        "AUTORIZADO" => EstadoComprobanteSri.Autorizado,
        "ANULADO" => EstadoComprobanteSri.Anulado,
        "NO AUTORIZADO" or "RECHAZADO" or "RECHAZADA" or "DEVUELTA" => EstadoComprobanteSri.NoAutorizado,
        _ => EstadoComprobanteSri.Otro,
    };

    /// <summary>
    /// Candidatos de una verificación masiva: dentro del alcance, de producción (las de pruebas no
    /// existen en el SRI real) y sin verificar o verificados hace más de <paramref name="umbral"/>.
    /// </summary>
    public static List<ComprobanteAVerificar> SeleccionarCandidatos(
        IEnumerable<ComprobanteAVerificar> comprobantes,
        IReadOnlyDictionary<(string Ruc, int RefId), EstadoSri> guardados,
        TimeSpan umbral, DateTime hoy, DateTime ahoraUtc) =>
        comprobantes
            .Where(c => c.Ambiente == 2 && EnAlcance(c.FechaEmision, hoy))
            .Where(c => !guardados.TryGetValue((c.Ruc, c.RefId), out var g)
                        || g.FechaVerificacion is null
                        || ahoraUtc - g.FechaVerificacion.Value > umbral)
            .ToList();

    // ---------------------------------------------------------------- lectura

    /// <summary>Estados ya guardados de una lista de comprobantes de un tipo, por (RUC, id).</summary>
    public async Task<IReadOnlyDictionary<(string Ruc, int RefId), EstadoSri>> ObtenerAsync(
        TipoComprobante tipo, IReadOnlyCollection<(string Ruc, int RefId)> items, CancellationToken ct = default)
    {
        var resultado = new Dictionary<(string, int), EstadoSri>();
        if (!Disponible || items.Count == 0) return resultado;

        var codDoc = Tipos.De(tipo).CodDoc;
        var rucs = items.Select(i => i.Ruc).Distinct().ToArray();
        var ids = items.Select(i => i.RefId).Distinct().ToArray();

        await using var db = new ConciliacionDbContext(_opcionesDb!);
        var filas = await db.EstadosSriComprobantes.AsNoTracking()
            .Where(e => e.CodDoc == codDoc && EF.Constant(rucs).Contains(e.Ruc) && EF.Constant(ids).Contains(e.RefId))
            .ToListAsync(ct);

        foreach (var f in filas)
        {
            resultado[(f.Ruc, f.RefId)] = new EstadoSri(f.Estado, f.Fuente, f.Detalle, f.FechaVerificacion, f.ClaveAcceso);
        }
        return resultado;
    }

    // ---------------------------------------------------------------- verificación

    /// <summary>Verifica un comprobante y guarda el resultado. Nunca lanza: los problemas quedan en <see cref="EstadoSri.Detalle"/>.</summary>
    public async Task<EstadoSri> VerificarAsync(ComprobanteAVerificar c, CancellationToken ct = default)
    {
        if (!Disponible)
        {
            return new EstadoSri(null, null, "La verificación en el SRI no está disponible en este servidor.", null, null);
        }

        var hoy = DateTime.Today;
        if (c.Ambiente != 2)
        {
            return new EstadoSri(null, null, "Comprobante de pruebas: no existe en el SRI real, no se verifica.", null, null);
        }
        if (!EnAlcance(c.FechaEmision, hoy))
        {
            return new EstadoSri(null, null, $"Emitido hace más de {AlcanceAnios} años: fuera del alcance de verificación.", null, null);
        }

        try
        {
            var clave = await ObtenerClaveGuardadaAsync(c, ct) ?? await ClaveDesdeDatilRequestsAsync(c, ct);

            // 1) Dentro del rango del SRI: el WS público es la fuente de verdad.
            if (EnRangoSri(c.FechaEmision, hoy))
            {
                if (clave is null)
                {
                    (clave, _) = await ConsultarDatilAsync(c, ct); // solo para recuperar la clave
                }
                if (clave is not null)
                {
                    var sri = await _verificadorSri!.VerificarAsync(clave, ct);
                    if (sri.Estado is not (EstadoComprobanteSri.ErrorServicio or EstadoComprobanteSri.FueraDeRango))
                    {
                        return await GuardarAsync(c, clave, sri.Estado, "SRI", sri.MensajeSri, sri.RespuestaCruda, ct);
                    }
                    if (sri.Estado == EstadoComprobanteSri.ErrorServicio)
                    {
                        return new EstadoSri(null, null, $"El SRI no respondió: {sri.MensajeSri}", null, clave, ConError: true);
                    }
                    // FueraDeRango inesperado: cae a Datil.
                }
            }

            // 2) Fuera del rango del SRI (o sin clave): Datil, dentro del alcance de 2 años.
            var (claveDatil, datil) = await ConsultarDatilAsync(c, ct);
            if (datil is null)
            {
                return new EstadoSri(null, null, "No se pudo consultar Datil (sin id de Datil o sin respuesta).", null, claveDatil ?? clave, ConError: true);
            }
            var estado = EstadoDesdeDatil(datil.Estado);
            if (estado == EstadoComprobanteSri.ErrorServicio)
            {
                return new EstadoSri(null, null, datil.Descripcion, null, claveDatil ?? clave, ConError: true);
            }
            return await GuardarAsync(c, claveDatil ?? clave, estado, "Datil", datil.Estado, datil.RawResponse, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Verificación de {Tipo} {RefId} ({Ruc})", c.Tipo, c.RefId, c.Ruc);
            return new EstadoSri(null, null, $"Error al verificar: {ex.Message}", null, null, ConError: true);
        }
    }

    /// <summary>
    /// Verifica muchos comprobantes (una masiva a la vez en todo el proceso, máx.
    /// <see cref="ConciliacionOptions.VerificacionConcurrenciaMaxima"/> llamadas simultáneas).
    /// Solo los candidatos de <see cref="SeleccionarCandidatos"/>.
    /// </summary>
    public async Task<ResumenVerificacionMasiva> VerificarVariosAsync(
        IReadOnlyList<ComprobanteAVerificar> candidatos, IProgress<int>? avance = null, CancellationToken ct = default)
    {
        if (!Disponible)
        {
            return new ResumenVerificacionMasiva(candidatos.Count, 0, 0, 0, 0, new[] { "La verificación en el SRI no está disponible." });
        }
        if (!await MasivaGate.WaitAsync(0, ct))
        {
            return new ResumenVerificacionMasiva(candidatos.Count, 0, 0, 0, 0, new[] { "Ya hay una verificación masiva en curso." });
        }

        try
        {
            var mensajes = new List<string>();
            int verificados = 0, anulados = 0, noVerificables = 0, errores = 0, hechos = 0;
            using var semaforo = new SemaphoreSlim(Math.Max(1, _opciones.VerificacionConcurrenciaMaxima));

            var tareas = candidatos.Select(async c =>
            {
                await semaforo.WaitAsync(ct);
                try
                {
                    var r = await VerificarAsync(c, ct);
                    lock (mensajes)
                    {
                        if (r.Estado is null)
                        {
                            if (r.ConError)
                            {
                                errores++;
                                mensajes.Add($"{Tipos.De(c.Tipo).Singular} {c.RefId}: {r.Detalle}");
                            }
                            else
                            {
                                noVerificables++;
                            }
                        }
                        else
                        {
                            verificados++;
                            if (r.Estado == nameof(EstadoComprobanteSri.Anulado)) anulados++;
                        }
                        avance?.Report(++hechos);
                    }
                }
                finally
                {
                    semaforo.Release();
                }
            }).ToList();

            await Task.WhenAll(tareas);
            return new ResumenVerificacionMasiva(candidatos.Count, verificados, anulados, noVerificables, errores, mensajes);
        }
        finally
        {
            MasivaGate.Release();
        }
    }

    public TimeSpan Umbral => TimeSpan.FromDays(_opciones.VerificacionUmbralDias);

    // ---------------------------------------------------------------- internos

    private async Task<string?> ObtenerClaveGuardadaAsync(ComprobanteAVerificar c, CancellationToken ct)
    {
        var codDoc = Tipos.De(c.Tipo).CodDoc;
        await using var db = new ConciliacionDbContext(_opcionesDb!);
        return await db.EstadosSriComprobantes.AsNoTracking()
            .Where(e => e.Ruc == c.Ruc && e.CodDoc == codDoc && e.RefId == c.RefId)
            .Select(e => e.ClaveAcceso)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<string?> ClaveDesdeDatilRequestsAsync(ComprobanteAVerificar c, CancellationToken ct)
    {
        var esRetencion = c.Tipo == TipoComprobante.Retencion;
        await using var db = await _peach.CreateDbContextAsync(ct);
        var jsons = await db.DatilRequests.AsNoTracking()
            .Where(r => r.RefId == c.RefId && r.IsTaxWithH == esRetencion)
            .OrderByDescending(r => r.Id)
            .Select(r => r.DatilRequest)
            .Take(5)
            .ToListAsync(ct);
        return jsons.Select(ClaveDesdeJson).FirstOrDefault(k => k is not null);
    }

    /// <summary>GET a Datil por su id: devuelve la clave de acceso (si vino) y la respuesta.</summary>
    private async Task<(string? Clave, DatilConsultaResult? Respuesta)> ConsultarDatilAsync(
        ComprobanteAVerificar c, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(c.DatilId)) return (null, null);

        var d = await _emisores.DatilAsync(c.Ruc, ct);
        var url = c.Tipo switch
        {
            TipoComprobante.Factura => d.FacturaUrl,
            TipoComprobante.NotaCredito => d.NotaCreditoUrl,
            TipoComprobante.Liquidacion => d.LiquidacionUrl,
            _ => d.RetencionUrl,
        };
        var respuesta = await _datil.ConsultarComprobanteAsync(c.DatilId, new DatilCredentials(d.ApiKey, d.Password, url), ct);
        return (ClaveDesdeJson(respuesta.RawResponse), respuesta);
    }

    private async Task<EstadoSri> GuardarAsync(
        ComprobanteAVerificar c, string? clave, EstadoComprobanteSri estado, string fuente,
        string? detalle, string? cruda, CancellationToken ct)
    {
        var codDoc = Tipos.De(c.Tipo).CodDoc;
        var ahora = DateTime.UtcNow;
        // La respuesta cruda solo se guarda cuando no es lo esperado (mantiene la tabla chica).
        var guardarCruda = estado is not EstadoComprobanteSri.Autorizado;

        for (var intento = 0; ; intento++)
        {
            await using var db = new ConciliacionDbContext(_opcionesDb!);
            var fila = await db.EstadosSriComprobantes
                .FirstOrDefaultAsync(e => e.Ruc == c.Ruc && e.CodDoc == codDoc && e.RefId == c.RefId, ct);
            if (fila is null)
            {
                fila = new EstadoSriComprobante { Ruc = c.Ruc, CodDoc = codDoc, RefId = c.RefId };
                db.EstadosSriComprobantes.Add(fila);
            }

            fila.ClaveAcceso = clave ?? fila.ClaveAcceso;
            fila.FechaEmision = DateOnly.FromDateTime(c.FechaEmision);
            fila.Estado = estado.ToString();
            fila.Fuente = fuente;
            fila.Detalle = detalle is { Length: > 500 } ? detalle[..500] : detalle;
            fila.RespuestaCruda = guardarCruda ? cruda : null;
            fila.FechaVerificacion = ahora;

            try
            {
                await db.SaveChangesAsync(ct);
                return new EstadoSri(fila.Estado, fila.Fuente, fila.Detalle, ahora, fila.ClaveAcceso);
            }
            catch (DbUpdateException) when (intento == 0)
            {
                // Carrera de dos verificaciones del mismo comprobante (índice único): reintenta como update.
            }
        }
    }
}
