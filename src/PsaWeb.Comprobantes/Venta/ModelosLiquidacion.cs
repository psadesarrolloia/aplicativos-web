using PsaWeb.Comprobantes.Proveedores;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>Código de porcentaje del SRI + tasa (fracción), resuelto del texto "12%"/"15%" (tabla <c>dicTaxRate</c>).</summary>
public sealed record TasaIva(string CodigoPorcentaje, double Porcentaje);

/// <summary>Busca una tasa de IVA por su nombre ("12%", "15%"…). Lo implementa la capa con EF.</summary>
public interface ITasaIvaLookup
{
    Task<TasaIva?> BuscarPorNombreAsync(string nombre, CancellationToken cancellationToken = default);
}

/// <summary>Cabecera de una liquidación de compra leída de Sage 50 (port de la parte de <c>Facturas</c> de <c>LoadPurchaseInvoice</c>).</summary>
public sealed record LiquidacionCabecera
{
    public string NumeroCompleto { get; init; } = string.Empty;
    public string Secuencial { get; init; } = string.Empty;
    public string CodigoEstablecimiento { get; init; } = string.Empty;
    public string PuntoEmision { get; init; } = string.Empty;
    public string VendorRecordNumber { get; init; } = string.Empty;
    public DateTime? FechaEmision { get; init; }
    public DateTime? FechaVencimiento { get; init; }
    public double IvaValor { get; init; }
    public double TotalSinImpuestos { get; init; }
    public double TotalConImpuestos { get; init; }
    public double BaseImponibleIva { get; init; }

    /// <summary>Código de porcentaje del IVA de la compra (de <c>dicTaxRate</c>; default "4").</summary>
    public string CodigoPorcentajeIva { get; init; } = "4";

    public string CodigoIva { get; init; } = "2";
}

/// <summary>Liquidación de compra completa leída de Sage 50. Equivale a <c>PurchaseLiqInvoice</c>.</summary>
public sealed class LiquidacionLeida
{
    public string PostOrderPeach { get; init; } = string.Empty;

    public LiquidacionCabecera Cabecera { get; init; } = new();

    public ProveedorSri? Proveedor { get; init; }

    /// <summary>Líneas (misma forma que factura; la liquidación no aplica descuento por línea).</summary>
    public IReadOnlyList<FacturaVentaLinea> Lineas { get; init; } = Array.Empty<FacturaVentaLinea>();

    public IReadOnlyList<string> Errores { get; init; } = Array.Empty<string>();

    public bool Ok =>
        Errores.Count == 0
        && Lineas.Count > 0
        && Proveedor is { Ok: true };
}
