namespace PsaWeb.Comprobantes.Sri;

/// <summary>
/// Tipo de comprobante sustento (viene en <c>JrnlHdr.ShipVia</c> de Sage 50) y su
/// código SRI. Port de <c>codDocs</c>.
/// </summary>
public static class CodigosDocumento
{
    public const string Factura = "FACTURA";
    public const string NotaDeVenta = "NOTA DE VENTA";
    public const string Liquidacion = "LIQUIDACION";

    /// <summary>Código SRI (01/02/03) o <c>null</c> si el tipo no es reconocido.</summary>
    public static string? CodigoDe(string? shipVia) => (shipVia?.Trim().ToUpperInvariant()) switch
    {
        Factura => "01",
        NotaDeVenta => "02",
        Liquidacion => "03",
        _ => null,
    };

    public static bool EsReconocido(string? shipVia) => CodigoDe(shipVia) is not null;
}
