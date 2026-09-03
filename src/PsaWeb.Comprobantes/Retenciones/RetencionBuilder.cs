using PsaWeb.Comprobantes.Proveedores;
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Comprobantes.Retenciones;

/// <summary>
/// Orquesta la construcción de una retención: resuelve el establecimiento y la
/// información adicional (vía lookups) y llama a <see cref="ConstructorRetencion"/>.
/// La lectura de Sage 50 la hace antes <see cref="LectorCompraRetencion"/>.
/// </summary>
public sealed class RetencionBuilder
{
    /// <summary>Código SRI del comprobante de retención.</summary>
    public const string CodDocRetencion = "07";

    private readonly IEstablecimientoLookup _establecimientos;
    private readonly IInfoAdicionalLookup _infoAdicional;

    public RetencionBuilder(IEstablecimientoLookup establecimientos, IInfoAdicionalLookup infoAdicional)
    {
        _establecimientos = establecimientos;
        _infoAdicional = infoAdicional;
    }

    /// <param name="emailPruebas">
    /// Casilla a la que se envía cuando <paramref name="ambiente"/> es 1 (pruebas);
    /// si está vacía se usa el email del proveedor.
    /// </param>
    public async Task<ResultadoRetencion> ArmarAsync(
        EmpresaEmisora emisor,
        DatosCompraRetencion compra,
        ProveedorSri proveedor,
        short ambiente,
        string? emailPruebas = null,
        CancellationToken cancellationToken = default)
    {
        var numRet = NumeroDocumentoSri.AnalizarFactura(compra.NumeroRetencion);

        EstablecimientoInfo? establecimiento = null;
        if (numRet.CodigoEstablecimiento.Length > 0)
        {
            establecimiento = await _establecimientos.BuscarAsync(
                emisor.Ruc, numRet.CodigoEstablecimiento, numRet.PuntoEmision, cancellationToken);
        }

        var infoAdicional = await _infoAdicional.ObtenerAsync(emisor.Ruc, CodDocRetencion, cancellationToken);

        var emailDestino = ambiente == 1 && !string.IsNullOrWhiteSpace(emailPruebas)
            ? emailPruebas!
            : proveedor.Email;

        return ConstructorRetencion.Construir(
            emisor, compra, proveedor, establecimiento, ambiente, emailDestino, infoAdicional);
    }
}
