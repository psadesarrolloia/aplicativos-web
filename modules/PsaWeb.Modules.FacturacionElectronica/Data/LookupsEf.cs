using Microsoft.EntityFrameworkCore;
using PsaWeb.Comprobantes.Retenciones;
using PsaWeb.Comprobantes.Venta;
using PsaWeb.Comprobantes.Venta.InfoAdicional;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.FacturacionElectronica.Data;

/// <summary>Establecimientos (tabla <c>Establishments</c>).</summary>
internal sealed class EstablecimientoLookupEf(IDbContextFactory<PeachEbillsContext> contextFactory)
    : IEstablecimientoLookup
{
    public async Task<EstablecimientoInfo?> BuscarAsync(
        string ruc, string codigo, string puntoEmision, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var est = await db.Establishments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Ruc == ruc && e.Code == codigo && e.IssuePoint == puntoEmision, cancellationToken);
        return est is null
            ? null
            : new EstablecimientoInfo(est.EstablishmentId, est.Code, est.IssuePoint, est.Address ?? string.Empty);
    }
}

/// <summary>Info adicional por documento (tabla <c>EPoofGeneralAditionalInfo</c>). Para NC y liquidaciones.</summary>
internal sealed class InfoAdicionalLookupEf(IDbContextFactory<PeachEbillsContext> contextFactory)
    : IInfoAdicionalLookup
{
    public async Task<IReadOnlyDictionary<string, string>?> ObtenerAsync(
        string ruc, string codDoc, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var filas = await db.EpoofGeneralAditionalInfo.AsNoTracking()
            .Where(x => x.Ruc == ruc && x.CodDoc == codDoc)
            .OrderBy(x => x.OrderNum)
            .Select(x => new { x.Nombre, x.ValueAllTime })
            .ToListAsync(cancellationToken);

        if (filas.Count == 0) return null;
        var dict = new Dictionary<string, string>();
        foreach (var f in filas) dict[f.Nombre] = f.ValueAllTime ?? string.Empty;
        return dict;
    }
}

/// <summary>Config de info adicional de facturas (tabla <c>InvoiceConfigAditionalInfo</c>).</summary>
internal sealed class ConfigInfoAdicionalFacturaEf(IDbContextFactory<PeachEbillsContext> contextFactory)
    : IConfigInfoAdicionalFactura
{
    public async Task<IReadOnlyList<ConfigInfoAdicional>> ObtenerAsync(
        string ruc, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var filas = await db.InvoiceConfigAditionalInfo.AsNoTracking()
            .Where(x => x.Ruc == ruc)
            .OrderBy(x => x.OrderNum)
            .Select(x => new ConfigInfoAdicional(x.Nombre, x.OrderNum, x.SourceTable, x.SourceValue, x.ValueAllTime))
            .ToListAsync(cancellationToken);
        return filas;
    }
}

/// <summary>Tasas de IVA (tabla <c>dicTaxRate</c>). Para liquidaciones de compra.</summary>
internal sealed class TasaIvaLookupEf(IDbContextFactory<PeachEbillsContext> contextFactory)
    : ITasaIvaLookup
{
    public async Task<TasaIva?> BuscarPorNombreAsync(string nombre, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var fila = await db.DicTaxRate.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Name == nombre, cancellationToken);
        return fila is null ? null : new TasaIva(fila.Id, fila.IvaRate);
    }
}
