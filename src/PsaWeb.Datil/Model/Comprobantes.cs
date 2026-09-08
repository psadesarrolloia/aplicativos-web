namespace PsaWeb.Datil.Model;

// Tipos compartidos por Factura / NotaCredito / Liquidacion.
// Port de DatilClientLibrary (Item, Impuesto, ImpuestoItem, InfoAdicional,
// RetencionFactura). Se serializan a snake_case sin nulos vía DatilJson.
// Reutilizan Emisor / Comprador / Establecimiento de Retencion.cs.

/// <summary>Ítem (línea) de un comprobante. Port de <c>DatilClientLibrary.Item</c>.</summary>
public sealed class ItemComprobante
{
    /// <summary>Código alfanumérico del comercio (máx. 25). Vacío si no aplica.</summary>
    public string CodigoPrincipal { get; set; } = string.Empty;

    public string CodigoAuxiliar { get; set; } = string.Empty;

    /// <summary>Sólo si aplica subsidio; si es null no se envía.</summary>
    public double? PrecioSinSubsidio { get; set; }

    public string Descripcion { get; set; } = string.Empty;

    public double Cantidad { get; set; }

    public double PrecioUnitario { get; set; }

    /// <summary><c>cantidad * precio_unitario</c>, antes de impuestos.</summary>
    public double PrecioTotalSinImpuestos { get; set; }

    public double Descuento { get; set; }

    /// <summary>Impuestos del ítem (con <see cref="Impuesto.Tarifa"/>).</summary>
    public List<Impuesto> Impuestos { get; set; } = new();

    /// <summary>Diccionario libre; si es null no se envía.</summary>
    public Dictionary<string, string>? DetallesAdicionales { get; set; }
}

/// <summary>
/// Impuesto aplicado a un ítem o a los totales. Port de
/// <c>DatilClientLibrary.Impuesto</c> / <c>ImpuestoItem</c> (mismo JSON):
/// en ítems se envía <see cref="Tarifa"/>; en totales se deja en null.
/// </summary>
public sealed class Impuesto
{
    public string Codigo { get; set; } = string.Empty;
    public string CodigoPorcentaje { get; set; } = string.Empty;
    public double BaseImponible { get; set; }
    public double Valor { get; set; }

    /// <summary>Porcentaje 0–100. Null en impuestos de totales (no se envía).</summary>
    public double? Tarifa { get; set; }
}

/// <summary>
/// Fila de información adicional con orden preservado (va como lista, no como
/// diccionario). Port de <c>DatilClientLibrary.InfoAdicional</c>.
/// </summary>
public sealed class InfoAdicionalItem
{
    public string Nombre { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
}

/// <summary>
/// Retención dentro de una factura (caso de derivados de petróleo / prensa).
/// Port de <c>DatilClientLibrary.RetencionFactura</c>. Rara vez se usa.
/// </summary>
public sealed class RetencionEnFactura
{
    public string Codigo { get; set; } = string.Empty;
    public string CodigoPorcentaje { get; set; } = string.Empty;
    public double Porcentaje { get; set; }
    public double Valor { get; set; }
}
