using Microsoft.EntityFrameworkCore;
using PsaWeb.Comprobantes.Retenciones;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.Retenciones.Data;

/// <summary>Busca establecimientos en la tabla <c>Establishments</c> de PeachEBills.</summary>
internal sealed class EstablecimientoLookupEf : IEstablecimientoLookup
{
    private readonly IDbContextFactory<PeachEbillsContext> _contextFactory;

    public EstablecimientoLookupEf(IDbContextFactory<PeachEbillsContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<EstablecimientoInfo?> BuscarAsync(
        string ruc, string codigo, string puntoEmision, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var est = await db.Establishments.AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.Ruc == ruc && e.Code == codigo && e.IssuePoint == puntoEmision,
                cancellationToken);

        return est is null
            ? null
            : new EstablecimientoInfo(est.EstablishmentId, est.Code, est.IssuePoint, est.Address ?? string.Empty);
    }
}

/// <summary>Info adicional por documento (tabla <c>EPoofGeneralAditionalInfo</c>). Port de <c>GeneralAdtionalInfo</c>.</summary>
internal sealed class InfoAdicionalLookupEf : IInfoAdicionalLookup
{
    private readonly IDbContextFactory<PeachEbillsContext> _contextFactory;

    public InfoAdicionalLookupEf(IDbContextFactory<PeachEbillsContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<IReadOnlyDictionary<string, string>?> ObtenerAsync(
        string ruc, string codDoc, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var filas = await db.EpoofGeneralAditionalInfo.AsNoTracking()
            .Where(x => x.Ruc == ruc && x.CodDoc == codDoc)
            .OrderBy(x => x.OrderNum)
            .Select(x => new { x.Nombre, x.ValueAllTime })
            .ToListAsync(cancellationToken);

        if (filas.Count == 0)
        {
            return null;
        }

        var dict = new Dictionary<string, string>();
        foreach (var f in filas)
        {
            dict[f.Nombre] = f.ValueAllTime ?? string.Empty;
        }
        return dict;
    }
}
