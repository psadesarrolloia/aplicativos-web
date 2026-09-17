namespace PsaWeb.Comprobantes.Compras;

/// <summary>
/// Bases imponibles y retenciones de IVA de una compra, leídas de las líneas
/// (<c>LineItem</c>/<c>JrnlRow</c>) del comprobante. Port de <c>LoadImponibles</c>
/// (<c>ATSfromPeach</c>) — compartido entre el ATS y Conciliación SRI (§13.1
/// del plan): no está acoplado al esquema del ATS, es un cálculo genérico de
/// "cuánto de esta compra es base gravada / no gravada / IVA".
/// </summary>
public sealed record BucketsCompra(
    decimal BaseNoGraIva,
    decimal BaseImponible,
    decimal BaseImpGrav,
    decimal BaseImpExe,
    decimal MontoIva,
    decimal ValRetBien10,
    decimal ValRetServ20,
    decimal ValorRetBienes,
    decimal ValRetServ50,
    decimal ValorRetServicios,
    decimal ValRetServ100)
{
    public static readonly BucketsCompra Cero = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Subtotal (todo menos IVA) — comparable contra <c>VALOR_SIN_IMPUESTOS</c> del reporte del SRI.</summary>
    public decimal Subtotal => BaseNoGraIva + BaseImponible + BaseImpGrav + BaseImpExe;

    /// <summary>Subtotal + IVA — comparable contra <c>IMPORTE_TOTAL</c> del reporte del SRI.</summary>
    public decimal Total => Subtotal + MontoIva;
}
