using PsaWeb.Comprobantes.Retenciones; // EmpresaEmisora, EstablecimientoInfo
using PsaWeb.Datil.Model;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>
/// Arma la <see cref="Liquidacion"/> de Datil y los datos a persistir a partir de
/// una <see cref="LiquidacionLeida"/>. Port de <c>DatilSend(ref PurchaseLiqInvoice)</c>.
/// Lógica pura (sin ODBC ni EF).
/// </summary>
public static class ConstructorLiquidacion
{
    public static ResultadoLiquidacion Construir(
        EmpresaEmisora emisor,
        EstablecimientoInfo establecimiento,
        LiquidacionLeida leida,
        short ambiente,
        string? emailPruebas,
        IReadOnlyDictionary<string, string>? informacionAdicional = null,
        string moneda = "USD",
        short tipoEmision = 1)
    {
        var errores = new List<string>(leida.Errores);
        if (leida.Proveedor is null)
        {
            errores.Add("La liquidación no tiene proveedor.");
            return ResultadoLiquidacion.ConErrores(errores);
        }
        errores.AddRange(leida.Proveedor.Errores);

        var cab = leida.Cabecera;

        if (cab.FechaEmision is null)
        {
            errores.Add("La liquidación no tiene fecha de emisión.");
        }

        var email = HelpersVenta.EmailDestino(leida.Proveedor.Email, ambiente, emailPruebas);

        // La liquidación usa el punto de emisión DEL NÚMERO (como la factura).
        var establecimientoDatil = new Establecimiento
        {
            Codigo = establecimiento.Codigo,
            PuntoEmision = cab.PuntoEmision,
            Direccion = establecimiento.Direccion,
        };

        var proveedorDatil = new Comprador
        {
            RazonSocial = leida.Proveedor.RazonSocial,
            Identificacion = leida.Proveedor.Identificacion,
            TipoIdentificacion = leida.Proveedor.TipoIdentificacion,
            Email = email,
            Direccion = leida.Proveedor.Direccion,
            Telefono = leida.Proveedor.Telefono,
        };

        var liq = new Liquidacion
        {
            Secuencial = HelpersVenta.SecuencialNumerico(cab.Secuencial),
            Moneda = moneda,
            Ambiente = ambiente,
            TipoEmision = tipoEmision,
            FechaEmision = new DateTimeOffset(cab.FechaEmision ?? default, HelpersVenta.ZonaEcuador),
            Emisor = HelpersVenta.Emisor(emisor, establecimientoDatil),
            Proveedor = proveedorDatil,
            Items = HelpersVenta.Items(leida.Lineas, cab.CodigoIva),
            Totales = new TotalesLiquidacion
            {
                TotalSinImpuestos = cab.TotalSinImpuestos,
                ImporteTotal = cab.TotalConImpuestos,
                Descuento = 0,
                Impuestos = HelpersVenta.AgruparImpuestos(leida.Lineas, cab.CodigoIva),
            },
            InformacionAdicional = informacionAdicional is { Count: > 0 }
                ? new Dictionary<string, string>(informacionAdicional)
                : null,
        };
        liq.Pagos.Add(new FormaPagoLiquidacion { FormaPago = "20", Total = cab.TotalConImpuestos });

        var guardar = new LiquidacionParaGuardar(
            NumeroCompleto: cab.NumeroCompleto,
            Secuencial: cab.Secuencial,
            EstablecimientoId: establecimiento.EstablishmentId,
            Moneda: moneda,
            FechaEmision: cab.FechaEmision ?? default,
            FechaVencimiento: cab.FechaVencimiento,
            Ambiente: ambiente,
            IssueType: tipoEmision,
            CodigoIva: cab.CodigoIva,
            CodigoPorcentajeIva: cab.CodigoPorcentajeIva,
            TotalSinImpuestos: cab.TotalSinImpuestos,
            BaseImponibleIva: cab.BaseImponibleIva,
            IvaValor: cab.IvaValor,
            TotalConImpuestos: cab.TotalConImpuestos,
            PostOrderPeach: leida.PostOrderPeach,
            Proveedor: HelpersVenta.Persona(new Clientes.ClienteSri
            {
                Identificacion = leida.Proveedor.Identificacion,
                TipoIdentificacion = leida.Proveedor.TipoIdentificacion,
                RazonSocial = leida.Proveedor.RazonSocial,
                Direccion = leida.Proveedor.Direccion,
                Email = leida.Proveedor.Email,
                Telefono = leida.Proveedor.Telefono,
            }),
            Lineas: HelpersVenta.LineasParaGuardar(leida.Lineas, cab.CodigoIva));

        return new ResultadoLiquidacion
        {
            Liquidacion = liq,
            NumeroCompleto = cab.NumeroCompleto,
            EstablecimientoId = establecimiento.EstablishmentId,
            Guardar = guardar,
            Errores = errores,
        };
    }
}
