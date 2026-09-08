using PsaWeb.Datil.Model;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>Resultado de <see cref="ConstructorLiquidacion.Construir"/>.</summary>
public sealed class ResultadoLiquidacion
{
    public Liquidacion? Liquidacion { get; init; }

    public string NumeroCompleto { get; init; } = string.Empty;

    public int EstablecimientoId { get; init; }

    public LiquidacionParaGuardar? Guardar { get; init; }

    public IReadOnlyList<string> Errores { get; init; } = Array.Empty<string>();

    public bool Ok => Errores.Count == 0 && Liquidacion is not null;

    public static ResultadoLiquidacion ConErrores(IEnumerable<string> errores) =>
        new() { Errores = errores.ToList() };
}

/// <summary>Datos planos para armar <c>Facturas</c> (codDoc 03, TransType 2) + <c>Details</c> + <c>Persons</c>.</summary>
public sealed record LiquidacionParaGuardar(
    string NumeroCompleto,
    string Secuencial,
    int EstablecimientoId,
    string Moneda,
    DateTime FechaEmision,
    DateTime? FechaVencimiento,
    short Ambiente,
    short IssueType,
    string CodigoIva,
    string CodigoPorcentajeIva,
    double TotalSinImpuestos,
    double BaseImponibleIva,
    double IvaValor,
    double TotalConImpuestos,
    string PostOrderPeach,
    PersonaParaGuardar Proveedor,
    IReadOnlyList<LineaFacturaParaGuardar> Lineas)
{
    public string CodDoc => "03";
    public int TransType => 2;
}
