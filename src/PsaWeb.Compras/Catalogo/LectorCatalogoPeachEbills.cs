using Microsoft.EntityFrameworkCore;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Compras.Catalogo;

/// <summary>Lee de PeachEBills los catálogos del registro de compras (solo lectura).</summary>
public static class LectorCatalogoPeachEbills
{
    public static async Task<CatalogoPeachEbills> LeerAsync(PeachEbillsContext db, CancellationToken cancellationToken = default)
    {
        var formas = await db.AboutApplyTwh.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new FormaPagoRetencion(x.Id, x.Description, x.PaymentDatilCodigo, x.HasTwh))
            .ToListAsync(cancellationToken);
        // El `.exe` resuelve la forma de pago del XML con FirstOrDefault sobre PaymentTypes: se respeta el orden por id.
        var tipos = await db.PaymentTypes.AsNoTracking().OrderBy(x => x.PaymentTypeId)
            .Where(x => x.PaymentSriCodigo != null)
            .Select(x => new TipoPagoSri(x.PaymentSriCodigo!.Trim(), x.PaymentDatilCodigo))
            .ToListAsync(cancellationToken);
        var tarifas = (await db.DicTaxRate.AsNoTracking().ToListAsync(cancellationToken))
            .Select(x => new TarifaIva(x.Id.Trim(), x.Name, Math.Round((decimal)x.IvaRate, 4)))
            .ToList();
        var impuestos = await db.DicImpuestosTipo.AsNoTracking().ToDictionaryAsync(x => x.Id.Trim(), x => x.Name, cancellationToken);
        return new CatalogoPeachEbills(formas, tipos, tarifas, impuestos);
    }

    /// <summary>Ítems aprendidos para un proveedor de una empresa (<c>GetAllVendorConfigurationQuery</c>).</summary>
    public static async Task<IReadOnlyList<ConfiguracionItemProveedor>> ConfiguracionesAsync(
        PeachEbillsContext db, string rucEmpresa, string identificacionProveedor, CancellationToken cancellationToken = default)
        => await db.VendorConfiguration.AsNoTracking()
            .Where(x => x.TransmitterRuc == rucEmpresa && x.VendorRuc == identificacionProveedor)
            .OrderBy(x => x.ConfigVendorId)
            .Select(x => new ConfiguracionItemProveedor(x.ConfigVendorId, x.TransmitterRuc, x.VendorRuc, x.VendorCode, x.SageItemId))
            .ToListAsync(cancellationToken);
}
