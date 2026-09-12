namespace PsaWeb.Ats.Ventas;

/// <summary>
/// Sumas de Sage 50 para un cliente + tipo de comprobante de ventas, antes de
/// armar la fila del ATS. Port de los <c>Load*</c> de <c>LoadSales</c>.
/// </summary>
/// <param name="BaseNoGraIva">Ventas no objeto de IVA (<c>SalesTaxType</c> 5 ó 6).</param>
/// <param name="BaseImponibleCruda">
/// Ventas gravadas 0% (<c>SalesTaxType</c> distinto de 0, 5 y 6). <b>No llega al
/// XML</b>: <c>LoadMontoIVA</c> pisa <c>baseImponible</c> a 0 justo después de
/// calcularla (bug del `.exe`, preservado — ver <see cref="ArmadorVentasAts"/>).
/// </param>
/// <param name="BaseImpGrav">Ventas gravadas con tarifa (<c>SalesTaxType</c> = 0).</param>
/// <param name="MontoIva">IVA de las líneas <c>RowType = 5</c>.</param>
/// <param name="ValorRetIva">Retención de IVA que le hicieron al informante en la venta (solo facturas).</param>
/// <param name="ValorRetRenta">Retención de renta que le hicieron al informante en la venta (solo facturas).</param>
public sealed record BucketsVentaAts(
    decimal BaseNoGraIva,
    decimal BaseImponibleCruda,
    decimal BaseImpGrav,
    decimal MontoIva,
    decimal ValorRetIva,
    decimal ValorRetRenta);
