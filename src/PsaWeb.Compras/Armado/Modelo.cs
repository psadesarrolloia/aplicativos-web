namespace PsaWeb.Compras.Armado;

/// <summary>Tipo de comprobante de compra (combo «Tipo» del `.exe`, <c>codDocs</c>): va a <c>ShipVia</c>.</summary>
public enum TipoDocumentoCompra { Factura, NotaDeVenta, Liquidacion }

/// <summary>Cómo se originó la OC (<c>LoadPurchaseOrder.LoadingTypes</c>): va a <c>ShipToZIP</c>.</summary>
public enum OrigenCompra
{
    /// <summary>Digitada a mano («Nueva»): <c>ShipToZIP = "Manual"</c>.</summary>
    Manual,
    /// <summary>XML cuyo comprador es la empresa: <c>ShipToZIP</c> vacío.</summary>
    Propia,
    /// <summary>XML emitido a otra identificación: <c>ShipToZIP = "Externo"</c>.</summary>
    Externa,
}

/// <summary>Sustento tributario (combo del `.exe`): <c>ShipToState</c> 01 crédito / 02 costo.</summary>
public enum SustentoCompra { Credito, Costo }

public static class TiposDocumentoCompra
{
    public static string Codigo(TipoDocumentoCompra tipo) => tipo switch
    {
        TipoDocumentoCompra.Factura => "01",
        TipoDocumentoCompra.NotaDeVenta => "02",
        _ => "03",
    };

    /// <summary>Texto de <c>ShipVia</c>.</summary>
    public static string Descripcion(TipoDocumentoCompra tipo) => tipo switch
    {
        TipoDocumentoCompra.Factura => "FACTURA",
        TipoDocumentoCompra.NotaDeVenta => "NOTA DE VENTA",
        _ => "LIQUIDACION",
    };
}

/// <summary>
/// Línea de detalle de la compra tal como la ve y edita el digitador (port de <c>PoDetailToLoad</c>). La web la
/// muestra en la grilla; el digitador puede elegir ítem, cuenta, retenciones y job.
/// </summary>
public sealed class LineaDetalle
{
    public string Descripcion { get; set; } = string.Empty;
    public string CodigoPrincipal { get; set; } = string.Empty;
    public string CodigoAuxiliar { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Descuento { get; set; }
    public decimal MontoSinImpuestos { get; set; }

    /// <summary><c>"2"</c> si la línea tiene IVA en el XML; <c>null</c> si no.</summary>
    public string? CodigoIva { get; set; }

    /// <summary>Código de porcentaje del SRI (0, 2, 3, 4, 5, 6, 7…).</summary>
    public string? CodigoPorcentajeIva { get; set; }

    /// <summary>Tarifa de IVA: 15 (desde el XML) o 0.15 (digitada, desde <c>dicTaxRate</c>). La retención de IVA acepta ambas.</summary>
    public decimal TarifaIva { get; set; }

    /// <summary>Ítem de Sage elegido por el digitador (o aprendido de <c>VendorConfiguration</c>). Vacío = ítem C automático.</summary>
    public string? ItemId { get; set; }

    public string? CuentaId { get; set; }
    public string? RetencionFuenteId { get; set; }
    public string? RetencionIvaId { get; set; }
    public string? JobId { get; set; }

    public LineaDetalle Copiar() => (LineaDetalle)MemberwiseClone();
}

/// <summary>Línea de impuesto (IVA, ICE…) de la compra (port de <c>PoTaxToLoad</c> en la grilla de impuestos).</summary>
public sealed class LineaImpuesto
{
    public string Codigo { get; set; } = string.Empty;
    public string? ItemId { get; set; }
    public string? CuentaId { get; set; }
    public decimal BaseImponible { get; set; }
    public decimal Valor { get; set; }
}

/// <summary>Línea de la grilla de retenciones (port de <c>PoTaxToLoad</c> en <c>dgvTwhDetails</c>).</summary>
/// <param name="Codigo"><c>"1"</c> renta, <c>"2"</c> IVA.</param>
/// <param name="CuentaAsumida">Cuenta de gasto de la retención asumida (forma de pago 7); vacía si no aplica.</param>
public sealed record LineaRetencion(string ItemId, string Codigo, string? CuentaId, decimal BaseImponible, decimal Valor, string CuentaAsumida);

public enum TipoLineaOc { Detalle, Impuesto, Propina, Autorizacion, Retencion, RetencionAsumida, Relleno }

/// <summary>Línea de la Purchase Order tal como se escribirá por SDK (en este orden).</summary>
/// <param name="CuentaId"><c>null</c> en las líneas «.»: Sage les pone la cuenta del proveedor.</param>
public sealed record LineaOc(
    TipoLineaOc Tipo,
    string? ItemId,
    string Descripcion,
    decimal Cantidad,
    decimal PrecioUnitario,
    decimal Monto,
    string? CuentaId,
    string? JobId);

/// <summary>Purchase Order armada, lista para el Bridge (F3). Campos de cabecera según §1.2 del plan.</summary>
public sealed record OcArmada(
    string Referencia,
    string ProveedorId,
    string NombreProveedor,
    string DireccionProveedor1,
    string DireccionProveedor2,
    string IdentificacionProveedor,
    DateTime Fecha,
    DateTime FechaRegistro,
    string ShipVia,
    string NumeroFactura,
    string? NumeroRetencion,
    string EstadoSustento,
    string? Zip,
    string CuentaPorPagar,
    IReadOnlyList<LineaOc> Lineas);
