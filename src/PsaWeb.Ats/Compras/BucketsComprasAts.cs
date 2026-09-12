namespace PsaWeb.Ats.Compras;

/// <summary>
/// Bases imponibles y retenciones de IVA de una compra, leídas de las líneas
/// (<c>LineItem</c>/<c>JrnlRow</c>) del comprobante. Port de <c>LoadImponibles</c>
/// (<c>ATSfromPeach</c>). A diferencia de ventas, acá <c>BaseImponible</c> no
/// tiene el Bug B1 — nada la pisa después de calcularla.
/// </summary>
public sealed record BucketsComprasAts(
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
    public static readonly BucketsComprasAts Cero = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
