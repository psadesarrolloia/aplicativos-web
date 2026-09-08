using PsaWeb.Datil.Model;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>Resultado de <see cref="ConstructorNotaCredito.Construir"/>.</summary>
public sealed class ResultadoNotaCredito
{
    public NotaCredito? NotaCredito { get; init; }

    public string NumeroCompleto { get; init; } = string.Empty;

    public int EstablecimientoId { get; init; }

    public NotaCreditoParaGuardar? Guardar { get; init; }

    public IReadOnlyList<string> Errores { get; init; } = Array.Empty<string>();

    public bool Ok => Errores.Count == 0 && NotaCredito is not null;

    public static ResultadoNotaCredito ConErrores(IEnumerable<string> errores) =>
        new() { Errores = errores.ToList() };
}

/// <summary>Datos planos para armar <c>Facturas</c> (codDoc 04) + <c>Details</c> + <c>Persons</c> + <c>NCdetail</c>.</summary>
public sealed record NotaCreditoParaGuardar(
    string NumeroCompleto,
    string Secuencial,
    int EstablecimientoId,
    string Moneda,
    DateTime FechaEmision,
    short Ambiente,
    short IssueType,
    string CodigoIva,
    string CodigoPorcentajeIva,
    double TotalSinImpuestos,
    double BaseImponibleIva,
    double IvaValor,
    double TotalConImpuestos,
    string PostOrderPeach,
    PersonaParaGuardar Persona,
    IReadOnlyList<LineaFacturaParaGuardar> Lineas,
    DocumentoModificadoParaGuardar DocumentoModificado)
{
    public string CodDoc => "04";
}

/// <summary>Fila de <c>NCdetail</c>.</summary>
public sealed record DocumentoModificadoParaGuardar(
    DateTime DateBill,
    string BillNumber,
    string BillCodeDoc,
    string Cause);
