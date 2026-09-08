using Microsoft.EntityFrameworkCore;
using PsaWeb.Comprobantes.Retenciones;
using PsaWeb.PeachEbills;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.FacturacionElectronica.Data;

/// <summary>Datos de Datil de una empresa para FE: clave/clave + URLs por tipo de comprobante + casilla de pruebas.</summary>
public sealed record DatilEmpresaFe(
    string ApiKey,
    string Password,
    string FacturaUrl,
    string NotaCreditoUrl,
    string LiquidacionUrl,
    string? EmailPruebas);

/// <summary>Lee de PeachEBills el emisor y la config de Datil por RUC (para el módulo FE).</summary>
public sealed class EmisorLookup(IDbContextFactory<PeachEbillsContext> contextFactory)
{
    /// <summary>El API de Datil no tiene URL de liquidación en la tabla; es constante.</summary>
    private const string UrlLiquidacionDatil = "https://link.datil.co/purchase-settlements/";

    public async Task<EmpresaEmisora> EmisorAsync(string ruc, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var t = await db.Transmitter.AsNoTracking().FirstOrDefaultAsync(x => x.Ruc == ruc, cancellationToken)
                ?? throw new InvalidOperationException($"No hay empresa (Transmitter) con RUC {ruc}.");

        return new EmpresaEmisora(
            Ruc: t.Ruc,
            RazonSocial: t.Name,
            NombreComercial: t.NameAlias,
            Direccion: t.Address,
            ContribuyenteEspecial: t.NumberResolutionCe ?? string.Empty,
            ObligadoContabilidad: t.HaveToDoAccounting);
    }

    public async Task<DatilEmpresaFe> DatilAsync(string ruc, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var api = await db.DatilApi.AsNoTracking().FirstOrDefaultAsync(x => x.Ruc == ruc, cancellationToken)
                  ?? throw new InvalidOperationException($"No hay configuración de Datil (DatilAPI) para el RUC {ruc}.");

        var emailPruebas = await db.CurrentAmbient.AsNoTracking()
            .Where(a => a.Ruc == ruc)
            .Select(a => a.EmailForTest)
            .FirstOrDefaultAsync(cancellationToken);

        return new DatilEmpresaFe(
            ApiKey: api.MyApiKey,
            Password: DbSecret.Decrypt(api.MySignaturePassword),
            FacturaUrl: api.ApiFacturaUrl,
            NotaCreditoUrl: api.ApiNcurl,
            LiquidacionUrl: UrlLiquidacionDatil,
            EmailPruebas: emailPruebas);
    }
}
