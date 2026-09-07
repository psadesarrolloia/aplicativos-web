using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.Retenciones.Data;

/// <summary>Una fila del tablero: empresa activa + cuántas retenciones tiene pendientes.</summary>
public sealed record FilaTablero(
    string Ruc,
    string Nombre,
    short Ambiente,
    int Pendientes,
    DateTime? UltimaEmision,
    string? UltimoNumero,
    string? Error);

/// <summary>Una retención ya guardada, para el historial reciente.</summary>
public sealed record RetencionReciente(
    int Thid,
    string Ruc,
    string Empresa,
    string Numero,
    DateTime Fecha,
    string Secuencial,
    string? ProveedorId,
    string? ProveedorNombre,
    string? DatilId,
    short Ambiente)
{
    /// <summary>true si la retención llegó a Datil (tiene id externo).</summary>
    public bool Emitida => !string.IsNullOrWhiteSpace(DatilId);
}

/// <summary>Una compra pendiente de generar la retención (referencia de Sage 50).</summary>
public sealed record RetencionPendiente(string Ruc, string Empresa, string Referencia, short Ambiente);

/// <summary>Una línea de la retención (renta / IVA).</summary>
public sealed record LineaRetencion(
    string Codigo,
    string CodigoPorcentaje,
    double Porcentaje,
    double BaseImponible,
    double ValorRetenido,
    string DocSustentoNumero,
    string DocSustentoCod,
    DateTime DocSustentoFecha);

/// <summary>Detalle completo de una retención guardada (para el popup).</summary>
public sealed record RetencionDetalle(
    int Thid,
    string Ruc,
    string Empresa,
    string Numero,
    string Secuencial,
    DateTime Fecha,
    string PeriodoFiscal,
    short Ambiente,
    string? ClaveAcceso,
    string? DatilId,
    int? EstablishmentId,
    string ProveedorId,
    string? ProveedorNombre,
    string? ProveedorTipo,
    string? ProveedorEmail,
    string? ProveedorTelefono,
    string? ProveedorDireccion,
    IReadOnlyList<LineaRetencion> Lineas)
{
    public bool Emitida => !string.IsNullOrWhiteSpace(DatilId);
    public double TotalRetenido => Lineas.Sum(l => l.ValorRetenido);
}

/// <summary>
/// Consultas de solo lectura para la página del módulo: panorama por empresa
/// (pendientes + última emisión) e historial reciente. No emite nada.
/// </summary>
public sealed class TableroRetenciones
{
    private readonly IDbContextFactory<PeachEbillsContext> _contextFactory;
    private readonly PendientesRepository _pendientes;
    private readonly RetencionesOptions _opciones;

    public TableroRetenciones(
        IDbContextFactory<PeachEbillsContext> contextFactory,
        PendientesRepository pendientes,
        IOptions<RetencionesOptions> opciones)
    {
        _contextFactory = contextFactory;
        _pendientes = pendientes;
        _opciones = opciones.Value;
    }

    public async Task<IReadOnlyList<FilaTablero>> PanoramaAsync(CancellationToken cancellationToken = default)
    {
        var empresas = await _pendientes.EmpresasActivasAsync(
            _opciones.OmitirRucs, _opciones.AmbienteForzado, cancellationToken);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var filas = new List<FilaTablero>(empresas.Count);
        foreach (var empresa in empresas)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int pendientes;
            string? error = null;
            try
            {
                pendientes = (await _pendientes.PendientesAsync(empresa.Ruc, empresa.Ambiente, cancellationToken)).Count;
            }
            catch (Exception ex)
            {
                pendientes = 0;
                error = ex.Message;
            }

            var ultima = await db.TaxWithHoldings.AsNoTracking()
                .Where(t => t.TransmitterRuc == empresa.Ruc && t.Ambient == empresa.Ambiente)
                .OrderByDescending(t => t.DateIssued)
                .ThenByDescending(t => t.Thid)
                .Select(t => new { t.DateIssued, t.NumberPech })
                .FirstOrDefaultAsync(cancellationToken);

            filas.Add(new FilaTablero(
                empresa.Ruc, empresa.Nombre, empresa.Ambiente, pendientes,
                ultima?.DateIssued, ultima?.NumberPech, error));
        }

        return filas;
    }

    /// <summary>
    /// Retenciones guardadas de todas las empresas, opcionalmente acotadas por
    /// rango de fechas de emisión (<paramref name="desde"/> inclusive,
    /// <paramref name="hasta"/> inclusive), las <paramref name="top"/> más nuevas.
    /// </summary>
    public Task<IReadOnlyList<RetencionReciente>> RecientesAsync(
        DateTime? desde = null, DateTime? hasta = null, int top = 200,
        CancellationToken cancellationToken = default)
        => RecientesInternoAsync(null, desde, hasta, top, cancellationToken);

    /// <summary>Igual que <see cref="RecientesAsync"/> pero de una sola empresa.</summary>
    public Task<IReadOnlyList<RetencionReciente>> RecientesPorEmpresaAsync(
        string ruc, DateTime? desde = null, DateTime? hasta = null, int top = 200,
        CancellationToken cancellationToken = default)
        => RecientesInternoAsync(ruc, desde, hasta, top, cancellationToken);

    private async Task<IReadOnlyList<RetencionReciente>> RecientesInternoAsync(
        string? rucFiltro, DateTime? desde, DateTime? hasta, int top, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.TaxWithHoldings.AsNoTracking();
        if (rucFiltro is not null)
        {
            query = query.Where(t => t.TransmitterRuc == rucFiltro);
        }
        if (desde is { } d)
        {
            query = query.Where(t => t.DateIssued >= d.Date);
        }
        if (hasta is { } h)
        {
            var finExclusivo = h.Date.AddDays(1);
            query = query.Where(t => t.DateIssued < finExclusivo);
        }

        var recientes = await query
            .OrderByDescending(t => t.DateIssued)
            .ThenByDescending(t => t.Thid)
            .Take(top)
            .Select(t => new
            {
                t.Thid,
                t.TransmitterRuc,
                t.NumberPech,
                t.DateIssued,
                t.Secuencial,
                t.Contact,
                t.DatilId,
                t.Ambient,
            })
            .ToListAsync(cancellationToken);

        var nombres = await db.Transmitter.AsNoTracking()
            .Select(x => new { x.Ruc, Nombre = x.NameAlias ?? x.Name })
            .ToListAsync(cancellationToken);
        var mapaNombre = nombres.ToDictionary(x => x.Ruc, x => x.Nombre);

        // Nombre del proveedor: (RUC emisor, identificación) -> Persons.Name.
        // EF.Constant fuerza un IN con literales (evita OPENJSON en SQL Server viejo).
        var rucs = recientes.Select(t => t.TransmitterRuc).Distinct().ToArray();
        var contactos = recientes
            .Select(t => t.Contact)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToArray();

        var mapaProveedor = new Dictionary<(string, string), string>();
        if (contactos.Length > 0)
        {
            var personas = await db.Persons.AsNoTracking()
                .Where(p => EF.Constant(rucs).Contains(p.Ructransmitter)
                         && EF.Constant(contactos).Contains(p.PersonId))
                .Select(p => new { p.Ructransmitter, p.PersonId, p.Name })
                .ToListAsync(cancellationToken);

            foreach (var p in personas)
            {
                mapaProveedor[(p.Ructransmitter, p.PersonId)] = p.Name;
            }
        }

        return recientes
            .Select(t => new RetencionReciente(
                t.Thid,
                t.TransmitterRuc,
                mapaNombre.GetValueOrDefault(t.TransmitterRuc, t.TransmitterRuc),
                t.NumberPech,
                t.DateIssued,
                t.Secuencial,
                t.Contact,
                t.Contact is not null && mapaProveedor.TryGetValue((t.TransmitterRuc, t.Contact), out var n) ? n : null,
                t.DatilId,
                t.Ambient))
            .ToList();
    }

    /// <summary>Detalle completo de una retención guardada para el popup.</summary>
    public async Task<RetencionDetalle?> DetalleAsync(int thId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var t = await db.TaxWithHoldings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Thid == thId, cancellationToken);
        if (t is null) return null;

        var empresa = await db.Transmitter.AsNoTracking()
            .Where(x => x.Ruc == t.TransmitterRuc)
            .Select(x => x.NameAlias ?? x.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? t.TransmitterRuc;

        var persona = await db.Persons.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Ructransmitter == t.TransmitterRuc && p.PersonId == t.Contact, cancellationToken);

        var lineas = await db.Thdetails.AsNoTracking()
            .Where(d => d.TaxWithHolding == thId)
            .OrderBy(d => d.ThdetailId)
            .Select(d => new LineaRetencion(
                d.Code ?? "", d.PercentCode ?? "", d.Percent, d.AmountInTaxes, d.RtaxValue,
                d.PurchaseNumber ?? "", d.PurchaseCodDoc ?? "", d.PurchaseDate))
            .ToListAsync(cancellationToken);

        return new RetencionDetalle(
            t.Thid, t.TransmitterRuc, empresa, t.NumberPech, t.Secuencial, t.DateIssued,
            t.Fperiodo, t.Ambient, t.ClaveAcceso, t.DatilId, t.TransmitterEstablishment,
            t.Contact,
            persona?.Name,
            persona?.Type,
            persona?.Email,
            persona?.Phone,
            persona?.Address,
            lineas);
    }

    /// <summary>
    /// Compras pendientes de generar la retención para una empresa: las primeras
    /// <paramref name="top"/> referencias y el total. Puede fallar si Sage 50 no
    /// está accesible (lo maneja el llamador).
    /// </summary>
    public async Task<(IReadOnlyList<RetencionPendiente> Filas, int Total)> PendientesDetalleAsync(
        string ruc, string empresa, short ambiente, int top = 200, CancellationToken cancellationToken = default)
    {
        var todas = await _pendientes.PendientesAsync(ruc, ambiente, cancellationToken);
        var filas = todas
            .Take(top)
            .Select(r => new RetencionPendiente(ruc, empresa, r, ambiente))
            .ToList();
        return (filas, todas.Count);
    }
}
