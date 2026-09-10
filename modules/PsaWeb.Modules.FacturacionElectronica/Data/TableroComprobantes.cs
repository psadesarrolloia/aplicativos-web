using Microsoft.EntityFrameworkCore;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.FacturacionElectronica.Data;

/// <summary>Un comprobante de venta ya guardado (para el listado).</summary>
public sealed record ComprobanteReciente(
    int FacturaId,
    string Ruc,
    string Empresa,
    string Numero,
    DateTime Fecha,
    string Secuencial,
    string? PersonaId,
    string? PersonaNombre,
    double Iva,
    double Total,
    string? DatilId,
    short Ambiente)
{
    public bool Emitido => !string.IsNullOrWhiteSpace(DatilId);
}

/// <summary>Una fila del panorama multi-empresa: empresa + cuántos comprobantes tiene guardados / pendientes.</summary>
public sealed record FilaPanorama(
    string Ruc,
    string Empresa,
    int Guardados,
    int Pendientes,
    DateTime? UltimaEmision,
    string? Error);

/// <summary>Una línea de un comprobante guardado.</summary>
public sealed record LineaComprobante(
    string Descripcion,
    double Cantidad,
    double PrecioUnitario,
    double Subtotal,
    double BaseIva,
    double IvaValor,
    double IvaPorcentaje,
    string CodigoPorcentajeIva,
    double Descuento);

/// <summary>Detalle completo de un comprobante guardado (para el popup).</summary>
public sealed record ComprobanteDetalle(
    int FacturaId,
    string Empresa,
    string CodDoc,
    string Numero,
    string Secuencial,
    DateTime Fecha,
    DateTime? Vencimiento,
    short Ambiente,
    string? DatilId,
    int? EstablishmentId,
    string? PersonaId,
    string? PersonaNombre,
    string? PersonaTipo,
    string? PersonaEmail,
    string? PersonaTelefono,
    string? PersonaDireccion,
    double TotalSinImpuestos,
    double IvaValor,
    double Total,
    string? DocModNumero,
    string? DocModTipo,
    DateTime? DocModFecha,
    string? DocModMotivo,
    IReadOnlyList<LineaComprobante> Lineas)
{
    public bool Emitido => !string.IsNullOrWhiteSpace(DatilId);
    public bool EsNotaCredito => CodDoc.Trim() == "04";
}

/// <summary>
/// Consultas de solo lectura sobre los comprobantes de venta ya guardados en
/// PeachEBills (<c>Facturas</c> + <c>Details</c> + <c>Persons</c> + <c>NCdetail</c>).
/// No emite nada. Análogo a <c>TableroRetenciones</c>.
/// </summary>
public sealed class TableroComprobantes(IDbContextFactory<PeachEbillsContext> contextFactory)
{
    /// <summary>Comprobantes de un tipo (<paramref name="codDoc"/>) de una empresa, del más nuevo al más viejo.</summary>
    public Task<IReadOnlyList<ComprobanteReciente>> RecientesAsync(
        string ruc, string codDoc, DateTime? desde = null, DateTime? hasta = null, int top = 200,
        CancellationToken cancellationToken = default)
        => RecientesAsync(new[] { ruc }, codDoc, desde, hasta, top, cancellationToken);

    /// <summary>Igual, pero de varias empresas (para el modo "ver todas").</summary>
    public async Task<IReadOnlyList<ComprobanteReciente>> RecientesAsync(
        IReadOnlyList<string> rucs, string codDoc, DateTime? desde, DateTime? hasta, int top,
        CancellationToken cancellationToken = default)
    {
        if (rucs.Count == 0) return Array.Empty<ComprobanteReciente>();

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var rucArr = rucs.Distinct().ToArray();
        var query = db.Facturas.AsNoTracking()
            .Where(f => f.CodDoc == codDoc && f.TransmitterRuc != null && EF.Constant(rucArr).Contains(f.TransmitterRuc));
        if (desde is { } d)
        {
            query = query.Where(f => f.DateIssued >= d.Date);
        }
        if (hasta is { } h)
        {
            var finExclusivo = h.Date.AddDays(1);
            query = query.Where(f => f.DateIssued < finExclusivo);
        }

        var filas = await query
            .OrderByDescending(f => f.DateIssued)
            .ThenByDescending(f => f.FacturaId)
            .Take(top)
            .Select(f => new
            {
                f.FacturaId,
                f.TransmitterRuc,
                f.FacturaNumberComplete,
                f.FacturaNumber,
                f.DateIssued,
                f.Comprador,
                f.IVAValue,
                f.TotalAmount,
                f.DatilId,
                f.Ambient,
            })
            .ToListAsync(cancellationToken);

        var nombres = await db.Transmitter.AsNoTracking()
            .Where(t => EF.Constant(rucArr).Contains(t.Ruc))
            .Select(t => new { t.Ruc, Nombre = t.NameAlias ?? t.Name })
            .ToListAsync(cancellationToken);
        var mapaEmpresa = nombres.ToDictionary(x => x.Ruc, x => x.Nombre);

        var ids = filas
            .Select(x => x.Comprador)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToArray();

        var mapaPersona = new Dictionary<(string, string), string>();
        if (ids.Length > 0)
        {
            var personas = await db.Persons.AsNoTracking()
                .Where(p => EF.Constant(rucArr).Contains(p.Ructransmitter) && EF.Constant(ids).Contains(p.PersonId))
                .Select(p => new { p.Ructransmitter, p.PersonId, p.Name })
                .ToListAsync(cancellationToken);
            foreach (var p in personas)
            {
                mapaPersona[(p.Ructransmitter, p.PersonId)] = p.Name;
            }
        }

        return filas
            .Select(x =>
            {
                var ruc = x.TransmitterRuc ?? string.Empty;
                return new ComprobanteReciente(
                    x.FacturaId,
                    ruc,
                    mapaEmpresa.GetValueOrDefault(ruc, ruc),
                    x.FacturaNumberComplete,
                    x.DateIssued,
                    x.FacturaNumber,
                    x.Comprador,
                    x.Comprador is not null && mapaPersona.TryGetValue((ruc, x.Comprador), out var n) ? n : null,
                    x.IVAValue,
                    x.TotalAmount,
                    x.DatilId,
                    x.Ambient);
            })
            .ToList();
    }

    /// <summary>
    /// Panorama por empresa (para el modo "ver todas"): comprobantes guardados +
    /// última emisión (SQL, barato) + pendientes de Sage 50 (por empresa, tolera
    /// fallos). Análogo a <c>TableroRetenciones.PanoramaAsync</c>.
    /// </summary>
    public async Task<IReadOnlyList<FilaPanorama>> PanoramaAsync(
        IReadOnlyList<(string Ruc, string Empresa)> empresas,
        string codDoc,
        Func<string, CancellationToken, Task<int>> contarPendientes,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rucArr = empresas.Select(e => e.Ruc).Distinct().ToArray();

        var agregados = await db.Facturas.AsNoTracking()
            .Where(f => f.CodDoc == codDoc && f.TransmitterRuc != null && EF.Constant(rucArr).Contains(f.TransmitterRuc))
            .GroupBy(f => f.TransmitterRuc!)
            .Select(g => new { Ruc = g.Key, Guardados = g.Count(), Ultima = g.Max(x => (DateTime?)x.DateIssued) })
            .ToListAsync(cancellationToken);
        var mapa = agregados.ToDictionary(x => x.Ruc, x => (x.Guardados, x.Ultima));

        var filas = new List<FilaPanorama>(empresas.Count);
        foreach (var (ruc, empresa) in empresas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (guardados, ultima) = mapa.TryGetValue(ruc, out var ag) ? ag : (0, (DateTime?)null);

            int pendientes = 0;
            string? error = null;
            try
            {
                pendientes = await contarPendientes(ruc, cancellationToken);
            }
            catch (Exception ex)
            {
                error = ex.Message.Contains("2301") || ex.Message.Contains("Cannot locate the named database")
                    ? "Sage 50 de esta empresa no disponible."
                    : ex.Message;
            }

            filas.Add(new FilaPanorama(ruc, empresa, guardados, pendientes, ultima, error));
        }
        return filas;
    }

    public async Task<HashSet<string>> PostOrdersEmitidosAsync(
        IReadOnlyList<string> rucs, string codDoc, CancellationToken cancellationToken = default)
    {
        if (rucs.Count == 0) return new HashSet<string>(StringComparer.Ordinal);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rucArr = rucs.Distinct().ToArray();
        var pos = await db.Facturas.AsNoTracking()
            .Where(f => f.CodDoc == codDoc && f.PostOrderPeach != null
                        && f.TransmitterRuc != null && EF.Constant(rucArr).Contains(f.TransmitterRuc))
            .Select(f => f.PostOrderPeach!)
            .ToListAsync(cancellationToken);
        return new HashSet<string>(pos, StringComparer.Ordinal);
    }

    /// <summary>
    /// <c>PostOrderPeach</c> de todos los comprobantes de un tipo ya emitidos por
    /// la empresa (sin filtro de fecha). Sirve para descontarlos de la lista de
    /// pendientes de Sage 50 y no mostrarlos duplicados.
    /// </summary>
    public async Task<HashSet<string>> PostOrdersEmitidosAsync(
        string ruc, string codDoc, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var pos = await db.Facturas.AsNoTracking()
            .Where(f => f.TransmitterRuc == ruc && f.CodDoc == codDoc && f.PostOrderPeach != null)
            .Select(f => f.PostOrderPeach!)
            .ToListAsync(cancellationToken);

        return new HashSet<string>(pos, StringComparer.Ordinal);
    }

    public async Task<ComprobanteDetalle?> DetalleAsync(int facturaId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var f = await db.Facturas.AsNoTracking().FirstOrDefaultAsync(x => x.FacturaId == facturaId, cancellationToken);
        if (f is null) return null;

        var empresa = await db.Transmitter.AsNoTracking()
            .Where(t => t.Ruc == f.TransmitterRuc)
            .Select(t => t.NameAlias ?? t.Name)
            .FirstOrDefaultAsync(cancellationToken);
        empresa ??= f.TransmitterRuc ?? string.Empty;

        var persona = await db.Persons.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Ructransmitter == f.TransmitterRuc && p.PersonId == f.Comprador, cancellationToken);

        var lineas = await db.Details.AsNoTracking()
            .Where(d => d.Factura == facturaId)
            .OrderBy(d => d.DetailId)
            .Select(d => new LineaComprobante(
                d.Description, d.Quantity, d.UnitPrice, d.AmountWithoutTAX,
                d.AmountForIVA, d.IVAValue, d.IVAPercent, d.PercentIVACode, d.Discount))
            .ToListAsync(cancellationToken);

        string? dmNum = null, dmTipo = null, dmMotivo = null;
        DateTime? dmFecha = null;
        if (f.CodDoc.Trim() == "04")
        {
            var nc = await db.NcDetails.AsNoTracking().FirstOrDefaultAsync(n => n.NCid == facturaId, cancellationToken);
            if (nc is not null)
            {
                dmNum = nc.BillNumber;
                dmTipo = nc.BillCodeDoc;
                dmMotivo = nc.Cause;
                dmFecha = nc.DateBill;
            }
        }

        return new ComprobanteDetalle(
            f.FacturaId, empresa, f.CodDoc, f.FacturaNumberComplete, f.FacturaNumber, f.DateIssued, f.DateDue,
            f.Ambient, f.DatilId, f.TransmitterEstablishment,
            f.Comprador, persona?.Name, persona?.Type, persona?.Email, persona?.Phone, persona?.Address,
            f.TotalWithoutTax, f.IVAValue, f.TotalAmount,
            dmNum, dmTipo, dmFecha, dmMotivo, lineas);
    }
}
