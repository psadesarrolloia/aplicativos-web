using PsaWeb.Ats.Esquema;

namespace PsaWeb.Ats.Compras;

/// <summary>Tipos de comprobante de compras del ATS (Tabla 4).</summary>
public static class TiposComprobanteComprasAts
{
    public const string Factura = "01";
    public const string NotaVenta = "02";
    public const string Liquidacion = "03";
    public const string NotaCredito = "04";
    public const string Desconocido = "00";
}

/// <summary>
/// Una compra ya leída de Sage 50 (proveedor, montos, retenciones, referencias
/// de documento), lista para que <see cref="ArmadorComprasAts"/> arme el
/// <c>detalleComprasType</c> del esquema. Reúne lo que hace
/// <c>ATSModel.LoadPurchases</c> antes de la parte puramente aritmética.
/// </summary>
public sealed record CompraCruda(
    string TipoComprobante,
    string CodSustento,
    string Establecimiento,
    string PuntoEmision,
    string Secuencial,
    string FechaRegistro,
    string FechaEmision,
    string Autorizacion,
    ProveedorAts Proveedor,
    BucketsComprasAts Buckets,
    string ShipToAddress2,
    string ShipToCity,
    detalleAirComprasType[]? RetencionesRenta,
    string? NumeroCompletoCompraOriginal,
    string? AutorizacionCompraOriginal);
