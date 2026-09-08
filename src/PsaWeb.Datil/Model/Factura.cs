namespace PsaWeb.Datil.Model;

/// <summary>
/// Factura de venta / liquidación en compra para el API de Datil.
/// Port de <c>DatilClientLibrary.Factura</c>. Se serializa a <c>snake_case</c>
/// sin nulos (ver <see cref="DatilJson"/>).
/// </summary>
public sealed class Factura
{
    public string Secuencial { get; set; } = string.Empty;

    public string Moneda { get; set; } = "USD";

    public DateTimeOffset FechaEmision { get; set; }

    /// <summary>Si es null, no se envía.</summary>
    public string? GuiaRemision { get; set; }

    /// <summary>1 = pruebas, 2 = producción.</summary>
    public int Ambiente { get; set; } = 1;

    /// <summary>1 = emisión normal, 2 = por indisponibilidad.</summary>
    public int TipoEmision { get; set; } = 1;

    public string Version { get; set; } = "1.0.0";

    /// <summary>Si es null, Datil la genera.</summary>
    public string? ClaveAcceso { get; set; }

    public double? ValorRetIva { get; set; }

    public double? ValorRetRenta { get; set; }

    public Emisor Emisor { get; set; } = new();

    public Comprador Comprador { get; set; } = new();

    public List<ItemComprobante> Items { get; set; } = new();

    /// <summary>Caso petróleo/prensa; si es null no se envía.</summary>
    public List<RetencionEnFactura>? Retenciones { get; set; }

    public TotalesFactura Totales { get; set; } = new();

    /// <summary>Información adicional con orden (lista <c>nombre</c>/<c>valor</c>).</summary>
    public List<InfoAdicionalItem>? InfoAdicional { get; set; }

    /// <summary>Información adicional como diccionario (claves tal cual).</summary>
    public Dictionary<string, string>? InformacionAdicional { get; set; }

    /// <summary>Crédito (si aplica). Excluyente con <see cref="Pagos"/> poblado.</summary>
    public CreditoFactura? Credito { get; set; }

    public List<MetodoPago> Pagos { get; set; } = new();
}

/// <summary>Totales de una factura. Port de <c>DatilClientLibrary.TotalesFactura</c>.</summary>
public sealed class TotalesFactura
{
    public double TotalSinImpuestos { get; set; }
    public double ImporteTotal { get; set; }
    public double Propina { get; set; }

    /// <summary>Suma de descuentos por ítem.</summary>
    public double Descuento { get; set; }

    public double? DescuentoAdicional { get; set; }

    /// <summary>Impuestos de totales (sin tarifa).</summary>
    public List<Impuesto> Impuestos { get; set; } = new();

    public double? TotalSubsidio { get; set; }
}

/// <summary>Método de pago. Port de <c>DatilClientLibrary.MetodoPago</c>.</summary>
public sealed class MetodoPago
{
    /// <summary>Código de forma de pago según Datil.</summary>
    public string Medio { get; set; } = string.Empty;

    public double Total { get; set; }

    public Dictionary<string, string>? Propiedades { get; set; }
}

/// <summary>Crédito otorgado en una factura. Port de <c>DatilClientLibrary.CreditoFactura</c>.</summary>
public sealed class CreditoFactura
{
    /// <summary>Fecha de vencimiento <c>yyyy-MM-dd</c>.</summary>
    public string FechaVencimiento { get; set; } = string.Empty;

    public double Monto { get; set; }
}
