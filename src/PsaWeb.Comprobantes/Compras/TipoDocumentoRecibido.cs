namespace PsaWeb.Comprobantes.Compras;

/// <summary>
/// Tipos de documento recibido que concilia el módulo Conciliación SRI. Las liquidaciones de compra y
/// las notas de débito recibidas no se contemplan (no hay casos en más de 5 años de Sage): si llegaran
/// en un reporte del SRI quedan como <see cref="Otro"/>.
/// </summary>
public enum TipoDocumentoRecibido { Factura, NotaCredito, Retencion, Otro }

public static class TiposDocumentoRecibido
{
    /// <summary>
    /// Interpreta el texto de la columna TIPO_COMPROBANTE del reporte del SRI ("Factura", "Nota de Crédito",
    /// "Comprobante de Retención"). Compara solo el inicio, sin tildes: el reporte viene en ISO-8859-1 y una
    /// mala decodificación del acento no debe cambiar el tipo.
    /// </summary>
    public static TipoDocumentoRecibido De(string? textoSri)
    {
        var t = textoSri?.Trim() ?? string.Empty;
        if (t.StartsWith("Factura", StringComparison.OrdinalIgnoreCase)) return TipoDocumentoRecibido.Factura;
        if (t.StartsWith("Nota de Cr", StringComparison.OrdinalIgnoreCase)) return TipoDocumentoRecibido.NotaCredito;
        if (t.StartsWith("Comprobante de Retenci", StringComparison.OrdinalIgnoreCase)) return TipoDocumentoRecibido.Retencion;
        return TipoDocumentoRecibido.Otro;
    }

    public static string Etiqueta(TipoDocumentoRecibido tipo) => tipo switch
    {
        TipoDocumentoRecibido.Factura => "Factura",
        TipoDocumentoRecibido.NotaCredito => "Nota de crédito",
        TipoDocumentoRecibido.Retencion => "Retención",
        _ => "Otro",
    };
}
