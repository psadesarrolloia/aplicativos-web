using PsaWeb.Datil.Model;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>Resultado de <see cref="ConstructorFactura.Construir"/>.</summary>
public sealed class ResultadoFactura
{
    /// <summary>Documento listo para <c>IDatilClient.EmitirFacturaAsync</c> (null si hay errores).</summary>
    public Factura? Factura { get; init; }

    public string NumeroCompleto { get; init; } = string.Empty;

    public int EstablecimientoId { get; init; }

    /// <summary>Datos a persistir en PeachEBills tras la emisión (null si hay errores).</summary>
    public FacturaParaGuardar? Guardar { get; init; }

    public IReadOnlyList<string> Errores { get; init; } = Array.Empty<string>();

    public bool Ok => Errores.Count == 0 && Factura is not null;

    public static ResultadoFactura ConErrores(IEnumerable<string> errores) =>
        new() { Errores = errores.ToList() };
}

/// <summary>
/// Datos planos para armar las entidades <c>Facturas</c> + <c>Details</c> +
/// <c>Persons</c> en la capa con EF (mantiene <c>PsaWeb.Comprobantes</c> sin EF).
/// </summary>
public sealed record FacturaParaGuardar(
    string CodDoc,
    string NumeroCompleto,
    string Secuencial,
    int EstablecimientoId,
    string Moneda,
    DateTime FechaEmision,
    DateTime? FechaVencimiento,
    short Ambiente,
    short IssueType,
    double TotalDescuento,
    string CodigoIva,
    string CodigoPorcentajeIva,
    double TotalSinImpuestos,
    double BaseImponibleIva,
    double IvaValor,
    double TotalConImpuestos,
    string PostOrderPeach,
    PersonaParaGuardar Persona,
    IReadOnlyList<LineaFacturaParaGuardar> Lineas);

public sealed record PersonaParaGuardar(
    string Identificacion,
    string TipoIdentificacion,
    string RazonSocial,
    string Direccion,
    string Email,
    string? Telefono,
    string? Fax);

public sealed record LineaFacturaParaGuardar(
    string Descripcion,
    string? CodigoPrincipal,
    string? CodigoAuxiliar,
    double Cantidad,
    double PrecioUnitario,
    double Descuento,
    double SubtotalSinImpuestos,
    string CodigoIva,
    string CodigoPorcentajeIva,
    double BaseImponibleIva,
    double IvaValor,
    double IvaPorcentaje);
