namespace PsaWeb.Comprobantes.Venta.InfoAdicional;

/// <summary>
/// Una fila de configuración de información adicional de facturas
/// (tabla <c>InvoiceConfigAditionalInfo</c> de PeachEBills). Define un campo:
/// nombre, orden, y de dónde sale el valor.
/// </summary>
/// <param name="Nombre">Etiqueta del campo en el comprobante.</param>
/// <param name="OrderNum">Orden relativo.</param>
/// <param name="SourceTable">
/// <c>null</c> = valor fijo (<paramref name="ValueAllTime"/>);
/// <c>"JrnlHdr"</c> / <c>"JrnlRow"</c> / <c>"Customers"</c> = de Sage 50.
/// </param>
/// <param name="SourceValue">Columna/expresión SQL dentro de <paramref name="SourceTable"/>.</param>
/// <param name="ValueAllTime">Valor fijo cuando no hay <paramref name="SourceTable"/>.</param>
public sealed record ConfigInfoAdicional(
    string Nombre,
    int OrderNum,
    string? SourceTable,
    string? SourceValue,
    string? ValueAllTime);

/// <summary>
/// Devuelve la configuración de información adicional de facturas de una empresa.
/// Lo implementa la capa con EF (lee <c>InvoiceConfigAditionalInfo</c>).
/// </summary>
public interface IConfigInfoAdicionalFactura
{
    Task<IReadOnlyList<ConfigInfoAdicional>> ObtenerAsync(
        string ruc, CancellationToken cancellationToken = default);
}
