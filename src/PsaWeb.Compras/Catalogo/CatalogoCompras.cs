using System.Globalization;

namespace PsaWeb.Compras.Catalogo;

/// <summary>Ítem de <c>LineItem</c> con los campos que usa el registro de compras (port de <c>sageItem</c>).</summary>
/// <param name="Clase"><c>LineItem.ItemClass</c> (0 = sin clase/descr., 1 = stock, 2 = …).</param>
/// <param name="CuentaInventario"><c>AccountID</c> de <c>InvAcctRecordNumber</c> (lo que el `.exe` llama «cuenta de compra» del ítem).</param>
public sealed record ItemSage(
    string Id,
    string Descripcion,
    int Clase,
    string Categoria,
    string CustomField1,
    string CustomField2,
    string CustomField3,
    string CustomField4,
    string CustomField5,
    string? CuentaInventario,
    bool Inactivo)
{
    /// <summary>
    /// % de retención de un ítem R-IRF/R-IVA: <c>CustomField2</c> (<c>"-2.75%"</c>) / 100 → <c>-0.0275</c>.
    /// Port de <c>SageContext.GetTwhPercent</c>, que usaba la cultura del equipo; acá se acepta punto o coma.
    /// </summary>
    public decimal PorcentajeRetencion
    {
        get
        {
            var texto = CustomField2.Split('%')[0].Trim().Replace(',', '.');
            return decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p / 100m : 0m;
        }
    }

    /// <summary>Código de impuesto de la retención: <c>"2"</c> si la categoría dice IVA, <c>"1"</c> si dice RF (port de <c>GetTwhTaxCode</c>).</summary>
    public string CodigoImpuestoRetencion =>
        Categoria.Contains("IVA") ? "2" : Categoria.Contains("RF") ? "1" : string.Empty;
}

/// <summary>Opción de «forma de pago / retención» (tabla <c>AboutApplyTwh</c> de PeachEBills).</summary>
public sealed record FormaPagoRetencion(int Id, string Descripcion, string? CodigoDatil, bool TieneRetencion)
{
    public const int TarjetaCredito = 1;
    public const int DebitoAutorizado = 2;
    public const int Otros = 5;
    public const int RetencionAsumida = 7;
}

/// <summary>Forma de pago del SRI → código de Datil (tabla <c>PaymentTypes</c>).</summary>
public sealed record TipoPagoSri(string CodigoSri, string CodigoDatil);

/// <summary>Tarifa de IVA por código de porcentaje del SRI (tabla <c>dicTaxRate</c>).</summary>
public sealed record TarifaIva(string CodigoPorcentaje, string Nombre, decimal Tasa);

/// <summary>Código de proveedor → ítem de Sage aprendido (tabla <c>VendorConfiguration</c>).</summary>
public sealed record ConfiguracionItemProveedor(int Id, string RucEmpresa, string IdentificacionProveedor, string CodigoProveedor, string ItemSageId);

/// <summary>
/// Catálogos con los que el `.exe` arma la OC, ya leídos. Cada lista respeta el filtro y el orden del `.exe`
/// (<c>ORDER BY ItemID</c>): la selección «primero que calce» depende de ese orden.
/// </summary>
public sealed class CatalogoCompras
{
    /// <summary>Ítems C: <c>CustomField1 = 'COMPRA'</c>, <b>incluye inactivos</b> (el `.exe` no filtra).</summary>
    public required IReadOnlyList<ItemSage> ItemsC { get; init; }

    /// <summary>Categoría <c>IMPUESTO</c>, activos (incluye <c>AUT-SRI</c>).</summary>
    public required IReadOnlyList<ItemSage> ItemsImpuesto { get; init; }

    /// <summary>Categoría <c>R-IRF</c>, activos.</summary>
    public required IReadOnlyList<ItemSage> ItemsRetencionFuente { get; init; }

    /// <summary>Categoría <c>R-IVA</c>, activos.</summary>
    public required IReadOnlyList<ItemSage> ItemsRetencionIva { get; init; }

    /// <summary>Ítems para los detalles: stock/no-stock/serializados de cualquier categoría salvo las especiales, sin <c>AUT-SRI</c>.</summary>
    public required IReadOnlyList<ItemSage> ItemsDetalle { get; init; }

    /// <summary>Ítem <c>AUT-SRI</c> (o <c>AUTSRI</c>) activo: su línea lleva la clave de acceso.</summary>
    public required ItemSage? ItemAutorizacion { get; init; }

    /// <summary>Primera cuenta del plan en el orden del SDK: la cuenta de la línea <c>AUT-SRI</c>.</summary>
    public required string PrimeraCuenta { get; init; }

    public required IReadOnlyList<FormaPagoRetencion> FormasPago { get; init; }
    public required IReadOnlyList<TipoPagoSri> TiposPagoSri { get; init; }
    public required IReadOnlyList<TarifaIva> TarifasIva { get; init; }

    /// <summary><c>dicImpuestosTipo</c>: código de impuesto → nombre («IVA», «ICE»…), la descripción de la línea de impuesto.</summary>
    public required IReadOnlyDictionary<string, string> TiposImpuesto { get; init; }

    public ItemSage? RetencionFuente(string? id) => id is null ? null : ItemsRetencionFuente.FirstOrDefault(x => x.Id == id);
    public ItemSage? RetencionIva(string? id) => id is null ? null : ItemsRetencionIva.FirstOrDefault(x => x.Id == id);

    /// <summary>Ítem de retención (fuente o IVA) por ID.</summary>
    public ItemSage? Retencion(string id) => RetencionFuente(id) ?? RetencionIva(id);

    public FormaPagoRetencion? FormaPago(int id) => FormasPago.FirstOrDefault(x => x.Id == id);
}
