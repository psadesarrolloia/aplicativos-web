using PsaWeb.Datil.Model;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>
/// Cabecera de una factura de venta leída de Sage 50, ya con el número validado
/// en formato y los totales/descuentos resueltos. Equivale a la parte de cabecera
/// del <c>Facturas</c> del <c>.exe</c> que arma <c>LoadSaleInvoice</c>.
/// </summary>
public sealed record FacturaVentaCabecera
{
    /// <summary>Número completo <c>000-000-000000000</c> (normalizado si venía corregible).</summary>
    public string NumeroCompleto { get; init; } = string.Empty;

    /// <summary>Secuencial de 9 dígitos.</summary>
    public string Secuencial { get; init; } = string.Empty;

    public string CodigoEstablecimiento { get; init; } = string.Empty;

    public string PuntoEmision { get; init; } = string.Empty;

    /// <summary><c>JrnlHdr.CustVendId</c> — se usa para leer el cliente.</summary>
    public string CustomerRecordNumber { get; init; } = string.Empty;

    public DateTime? FechaEmision { get; init; }

    public DateTime? FechaVencimiento { get; init; }

    /// <summary>Total con impuestos (|MainAmount|).</summary>
    public double TotalConImpuestos { get; init; }

    public double IvaValor { get; init; }

    /// <summary>Total sin impuestos (TotalConImpuestos - IvaValor).</summary>
    public double TotalSinImpuestos { get; init; }

    /// <summary>Base imponible gravada con IVA (suma de las líneas con IVA, menos descuento con IVA).</summary>
    public double BaseImponibleIva { get; init; }

    public double DescuentoTotal { get; init; }

    /// <summary>Código de porcentaje de IVA del SRI ("0" sin IVA, "2"/"3"/"4"… con IVA).</summary>
    public string CodigoPorcentajeIva { get; init; } = "0";

    /// <summary>Código de impuesto IVA del SRI (fijo "2").</summary>
    public string CodigoIva { get; init; } = "2";
}

/// <summary>Línea (ítem) de una factura de venta leída de Sage 50, con descuento ya aplicado.</summary>
public sealed record FacturaVentaLinea
{
    public string Descripcion { get; init; } = string.Empty;
    public double Cantidad { get; init; }
    public double PrecioUnitario { get; init; }

    /// <summary>Subtotal sin impuestos de la línea (ya con el descuento restado).</summary>
    public double SubtotalSinImpuestos { get; init; }

    /// <summary>Base imponible para IVA de la línea.</summary>
    public double BaseImponibleIva { get; init; }

    public double IvaValor { get; init; }

    /// <summary>Porcentaje de IVA como fracción (0.12, 0.15, 0 sin IVA).</summary>
    public double IvaPorcentaje { get; init; }

    public string CodigoPorcentajeIva { get; init; } = "0";

    public string CodigoPrincipal { get; init; } = "0";

    public string CodigoAuxiliar { get; init; } = string.Empty;

    public double Descuento { get; init; }
}

/// <summary>
/// Factura de venta completa leída de Sage 50: cabecera + cliente + líneas +
/// información adicional + errores detectados. Equivale al <c>SaleInvoice</c>
/// del <c>.exe</c>. Si <see cref="Ok"/> es false no se debe emitir.
/// </summary>
public sealed class FacturaVentaLeida
{
    public string PostOrderPeach { get; init; } = string.Empty;

    public FacturaVentaCabecera Cabecera { get; init; } = new();

    public PsaWeb.Comprobantes.Clientes.ClienteSri? Cliente { get; init; }

    public IReadOnlyList<FacturaVentaLinea> Lineas { get; init; } = Array.Empty<FacturaVentaLinea>();

    public IReadOnlyList<InfoAdicionalItem> InfoAdicional { get; init; } = Array.Empty<InfoAdicionalItem>();

    /// <summary>Problemas encontrados al leer/armar (los <c>Mistake</c> del <c>.exe</c>).</summary>
    public IReadOnlyList<string> Errores { get; init; } = Array.Empty<string>();

    /// <summary>true si no hay errores propios ni del cliente y hay al menos una línea.</summary>
    public bool Ok =>
        Errores.Count == 0
        && Lineas.Count > 0
        && Cliente is { Ok: true };
}
