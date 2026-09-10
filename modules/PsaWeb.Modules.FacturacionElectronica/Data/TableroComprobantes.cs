using Microsoft.EntityFrameworkCore;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.FacturacionElectronica.Data;

/// <summary>Un comprobante de venta ya guardado (para el listado).</summary>
public sealed record ComprobanteReciente(
    int FacturaId,
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
    public async Task<IReadOnlyList<ComprobanteReciente>> RecientesAsync(
        string ruc, string codDoc, DateTime? desde = null, DateTime? hasta = null, int top = 200,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Facturas.AsNoTracking()
            .Where(f => f.TransmitterRuc == ruc && f.CodDoc == codDoc);
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

        var ids = filas
            .Select(x => x.Comprador)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToArray();

        var mapa = new Dictionary<string, string>();
        if (ids.Length > 0)
        {
            var personas = await db.Persons.AsNoTracking()
                .Where(p => p.Ructransmitter == ruc && EF.Constant(ids).Contains(p.PersonId))
                .Select(p => new { p.PersonId, p.Name })
                .ToListAsync(cancellationToken);
            foreach (var p in personas)
            {
                mapa[p.PersonId] = p.Name;
            }
        }

        return filas
            .Select(x => new ComprobanteReciente(
                x.FacturaId,
                x.FacturaNumberComplete,
                x.DateIssued,
                x.FacturaNumber,
                x.Comprador,
                x.Comprador is not null && mapa.TryGetValue(x.Comprador, out var n) ? n : null,
                x.IVAValue,
                x.TotalAmount,
                x.DatilId,
                x.Ambient))
            .ToList();
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
