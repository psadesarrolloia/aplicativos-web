using PsaWeb.Comprobantes.Retenciones; // IEstablecimientoLookup / EstablecimientoInfo (DTOs genéricos; podrían mudarse a un namespace neutral).

namespace PsaWeb.Comprobantes.Sri;

/// <summary>
/// Valida el número de un comprobante de venta (<c>000-000-000000000</c>) y
/// resuelve su establecimiento en la tabla <c>Establishments</c> de PeachEBills.
/// Port de <c>CommonSriNumberValidate</c> / <c>SalesInvoiceNumberValidate</c>
/// (usa el formato tolerante <see cref="NumeroDocumentoSri.AnalizarFactura"/>,
/// igual que facturas, notas de crédito y liquidaciones en el <c>.exe</c>).
/// </summary>
public sealed class ValidadorNumeroEstablecimiento
{
    private ValidadorNumeroEstablecimiento(
        NumeroDocumentoSri numero, bool esValido, int? establecimientoId, string? error)
    {
        Numero = numero;
        EsValido = esValido;
        EstablecimientoId = establecimientoId;
        Error = error;
    }

    public NumeroDocumentoSri Numero { get; }

    /// <summary>Formato correcto Y establecimiento encontrado.</summary>
    public bool EsValido { get; }

    public bool FueCorregido => Numero.FueCorregido;

    public string CodigoEstablecimiento => Numero.CodigoEstablecimiento;

    public string PuntoEmision => Numero.PuntoEmision;

    public string Secuencial => Numero.Secuencial;

    /// <summary><c>EstablishmentId</c> de PeachEBills; null si no es válido.</summary>
    public int? EstablecimientoId { get; }

    public string? Error { get; }

    public static async Task<ValidadorNumeroEstablecimiento> CrearAsync(
        string ruc,
        string numeroCompleto,
        IEstablecimientoLookup lookup,
        CancellationToken cancellationToken = default)
    {
        var numero = NumeroDocumentoSri.AnalizarFactura(numeroCompleto);
        if (!numero.EsValido)
        {
            return new ValidadorNumeroEstablecimiento(
                numero, esValido: false, establecimientoId: null,
                error: "Formato de número incorrecto. Revise el formato: ___-___-_________ (123-123-123456789).");
        }

        var establecimiento = await lookup.BuscarAsync(
            ruc, numero.CodigoEstablecimiento, numero.PuntoEmision, cancellationToken);

        if (establecimiento is null)
        {
            return new ValidadorNumeroEstablecimiento(
                numero, esValido: false, establecimientoId: null,
                error: $"No se encontró establecimiento registrado: {numero.CodigoEstablecimiento}-{numero.PuntoEmision}.");
        }

        return new ValidadorNumeroEstablecimiento(
            numero, esValido: true, establecimientoId: establecimiento.EstablishmentId, error: null);
    }
}
