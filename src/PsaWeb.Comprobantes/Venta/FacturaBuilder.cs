using PsaWeb.Comprobantes.Retenciones; // EmpresaEmisora, IEstablecimientoLookup
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>
/// Orquesta el armado de una factura de venta: resuelve el establecimiento por el
/// número (tabla <c>Establishments</c>) y llama a <see cref="ConstructorFactura"/>.
/// La lectura de Sage 50 (<see cref="LectorFacturaVenta.LeerAsync"/>, que ya trae
/// el cliente y la información adicional) la hace quien invoca. Equivale a la
/// parte de resolución de <c>DatilSend(ref SaleInvoice)</c>.
/// </summary>
public sealed class FacturaBuilder
{
    private readonly IEstablecimientoLookup _establecimientos;

    public FacturaBuilder(IEstablecimientoLookup establecimientos)
        => _establecimientos = establecimientos;

    public async Task<ResultadoFactura> ArmarAsync(
        EmpresaEmisora emisor,
        FacturaVentaLeida leida,
        short ambiente,
        string? emailPruebas,
        string codigoFormaPagoDatil,
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
            return ResultadoFactura.ConErrores(errores);
        }

        return ConstructorFactura.Construir(
            emisor, validador.Establecimiento, leida, ambiente, emailPruebas,
            codigoFormaPagoDatil, moneda, tipoEmision);
    }
}
