using PsaWeb.Comprobantes.Clientes;
using PsaWeb.Datil.Model;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>Cabecera de una nota de crédito de venta leída de Sage 50 (port de la parte de <c>Facturas</c> que arma <c>LoadSaleNC</c>).</summary>
public sealed record NotaCreditoCabecera
{
    public string NumeroCompleto { get; init; } = string.Empty;
    public string Secuencial { get; init; } = string.Empty;
    public string CodigoEstablecimiento { get; init; } = string.Empty;
    public string PuntoEmision { get; init; } = string.Empty;
    public string CustomerRecordNumber { get; init; } = string.Empty;
    public DateTime? FechaEmision { get; init; }
    public double IvaValor { get; init; }

    /// <summary>Suma de <c>AmountWithoutTAX</c> de las líneas.</summary>
    public double TotalSinImpuestos { get; init; }

    /// <summary><c>TotalSinImpuestos + IvaValor</c>, redondeado.</summary>
    public double TotalConImpuestos { get; init; }

    public double BaseImponibleIva { get; init; }

    public string CodigoPorcentajeIva { get; init; } = "0";

    public string CodigoIva { get; init; } = "2";
}

/// <summary>
/// Datos del documento (factura) que la nota de crédito modifica. Se persisten en
/// la tabla <c>NCdetail</c> y se envían a Datil como <c>documento_modificado</c>.
/// </summary>
public sealed record DocumentoModificado(
    string BillNumber,
    string BillCodeDoc,
    DateTime DateBill,
    string Cause);

/// <summary>Nota de crédito de venta completa leída de Sage 50. Equivale a <c>SaleNC</c>.</summary>
public sealed class NotaCreditoLeida
{
    public string PostOrderPeach { get; init; } = string.Empty;

    public NotaCreditoCabecera Cabecera { get; init; } = new();

    /// <summary>Referencia a la factura modificada (para <c>NCdetail</c> y Datil).</summary>
    public DocumentoModificado DocumentoModificado { get; init; } = new(string.Empty, "01", default, "Devolución");

    public ClienteSri? Cliente { get; init; }

    /// <summary>Líneas (misma forma que factura; la NC no tiene descuento por línea).</summary>
    public IReadOnlyList<FacturaVentaLinea> Lineas { get; init; } = Array.Empty<FacturaVentaLinea>();

    public IReadOnlyList<string> Errores { get; init; } = Array.Empty<string>();

    public bool Ok =>
        Errores.Count == 0
        && Lineas.Count > 0
        && Cliente is { Ok: true }
        && !string.IsNullOrEmpty(DocumentoModificado.BillNumber);
}
