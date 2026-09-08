namespace PsaWeb.Datil.Model;

/// <summary>
/// Liquidación de compra para el API de Datil.
/// Port de <c>DatilClientLibrary.LiquidacionCompra</c>. Se serializa a
/// <c>snake_case</c> sin nulos (ver <see cref="DatilJson"/>).
/// </summary>
public sealed class Liquidacion
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

    public Emisor Emisor { get; set; } = new();

    /// <summary>Proveedor (mismo JSON que <see cref="Comprador"/>).</summary>
    public Comprador Proveedor { get; set; } = new();

    public List<ItemComprobante> Items { get; set; } = new();

    public TotalesLiquidacion Totales { get; set; } = new();

    /// <summary>Información adicional como diccionario (claves tal cual).</summary>
    public Dictionary<string, string>? InformacionAdicional { get; set; }

    public List<FormaPagoLiquidacion> Pagos { get; set; } = new();
}

/// <summary>Totales de una liquidación. Port de <c>DatilClientLibrary.TotalesLiquidacion</c>.</summary>
public sealed class TotalesLiquidacion
{
    public double TotalSinImpuestos { get; set; }
    public double ImporteTotal { get; set; }
    public double Descuento { get; set; }
    public double? DescuentoAdicional { get; set; }

    /// <summary>Impuestos de totales (sin tarifa).</summary>
    public List<Impuesto> Impuestos { get; set; } = new();

    public double? TotalSubsidio { get; set; }
}

/// <summary>Forma de pago de una liquidación. Port de <c>DatilClientLibrary.FormaPagoLiquidacionC</c>.</summary>
public sealed class FormaPagoLiquidacion
{
    /// <summary>Código del tipo de forma de pago (requerido).</summary>
    public string FormaPago { get; set; } = string.Empty;

    public double Total { get; set; }

    /// <summary>Máx. 10 caracteres; si es null no se envía.</summary>
    public string? UnidadTiempo { get; set; }

    /// <summary>Máx. 14 caracteres; si es null no se envía.</summary>
    public string? Plazo { get; set; }

    public Dictionary<string, string>? Propiedades { get; set; }
}
