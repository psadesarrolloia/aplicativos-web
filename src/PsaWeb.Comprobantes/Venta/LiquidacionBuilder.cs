using PsaWeb.Comprobantes.Retenciones; // EmpresaEmisora, IEstablecimientoLookup, IInfoAdicionalLookup
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>
/// Orquesta el armado de una liquidación de compra: resuelve el establecimiento
/// por el número (formato tolerante), la información adicional del tipo de
/// documento (<c>03</c>) y llama a <see cref="ConstructorLiquidacion"/>. Equivale
/// a la parte de resolución de <c>DatilSend(ref PurchaseLiqInvoice)</c>.
/// </summary>
public sealed class LiquidacionBuilder
{
    private const string CodDocLiq = "03";

    private readonly IEstablecimientoLookup _establecimientos;
    private readonly IInfoAdicionalLookup _infoAdicional;

    public LiquidacionBuilder(IEstablecimientoLookup establecimientos, IInfoAdicionalLookup infoAdicional)
    {
        _establecimientos = establecimientos;
        _infoAdicional = infoAdicional;
    }

    public async Task<ResultadoLiquidacion> ArmarAsync(
        EmpresaEmisora emisor,
        LiquidacionLeida leida,
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
            if (leida.Proveedor is not null) errores.AddRange(leida.Proveedor.Errores);
            errores.Add(validador.Error ?? "No se pudo resolver el establecimiento.");
            return ResultadoLiquidacion.ConErrores(errores);
        }

        var info = await _infoAdicional.ObtenerAsync(emisor.Ruc, CodDocLiq, cancellationToken);

        return ConstructorLiquidacion.Construir(
            emisor, validador.Establecimiento, leida, ambiente, emailPruebas, info, moneda, tipoEmision);
    }
}
