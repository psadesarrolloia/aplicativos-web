namespace PsaWeb.Ats.Ventas;

/// <summary>
/// Una fila de ventas ya leída de Sage 50 (cliente + tipo de comprobante +
/// montos), antes de armar el <c>detalleVentasType</c> del esquema y de
/// fusionar por cliente duplicado (<see cref="ArmadorVentasAts"/>).
/// </summary>
/// <param name="TipoComprobante">"18" (factura) o "04" (nota de crédito).</param>
/// <param name="NumeroComprobantes">
/// Texto tal cual lo entrega Sage (<c>COUNT(DISTINCT PostOrder)</c> convertido a
/// string) — el esquema lo declara <c>string</c>, no numérico.
/// </param>
public sealed record FilaVentaCruda(
    string TipoComprobante,
    string NumeroComprobantes,
    ClienteAts Cliente,
    BucketsVentaAts Buckets);
