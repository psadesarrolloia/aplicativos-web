using PsaWeb.Comprobantes.Retenciones; // EmpresaEmisora, IEstablecimientoLookup, IInfoAdicionalLookup
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>
/// Orquesta el armado de una nota de crédito de venta: resuelve el establecimiento
/// por el número (formato estricto), la información adicional del tipo de documento
/// (<c>04</c>) y llama a <see cref="ConstructorNotaCredito"/>. Equivale a la parte
/// de resolución de <c>DatilSend(ref SaleNC)</c>.
/// </summary>
public sealed class NotaCreditoBuilder
{
    private const string CodDocNc = "04";

    private readonly IEstablecimientoLookup _establecimientos;
    private readonly IInfoAdicionalLookup _infoAdicional;

    public NotaCreditoBuilder(IEstablecimientoLookup establecimientos, IInfoAdicionalLookup infoAdicional)
    {
        _establecimientos = establecimientos;
        _infoAdicional = infoAdicional;
    }

    public async Task<ResultadoNotaCredito> ArmarAsync(
        EmpresaEmisora emisor,
        NotaCreditoLeida leida,
        short ambiente,
        string? emailPruebas,
        string moneda = "USD",
        short tipoEmision = 1,
        CancellationToken cancellationToken = default)
    {
        var validador = await ValidadorNumeroEstablecimiento.CrearAsync(
            emisor.Ruc, leida.Cabecera.NumeroCompleto, _establecimientos, cancellationToken);

        if (!validador.EsValido || validador.Establecimiento is null)
        {
            var errores = new List<string>(leida.Errores);
            if (leida.Cliente is not null) errores.AddRange(leida.Cliente.Errores);
            errores.Add(validador.Error ?? "No se pudo resolver el establecimiento.");
            return ResultadoNotaCredito.ConErrores(errores);
        }

        var info = await _infoAdicional.ObtenerAsync(emisor.Ruc, CodDocNc, cancellationToken);

        return ConstructorNotaCredito.Construir(
            emisor, validador.Establecimiento, leida, ambiente, emailPruebas, info, moneda, tipoEmision);
    }
}
