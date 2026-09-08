using PsaWeb.Comprobantes.Retenciones; // EmpresaEmisora, EstablecimientoInfo
using PsaWeb.Datil.Model;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>
/// Arma la <see cref="NotaCredito"/> de Datil y los datos a persistir a partir de
/// una <see cref="NotaCreditoLeida"/>. Port de <c>DatilSend(ref SaleNC)</c>.
/// Lógica pura (sin ODBC ni EF).
/// </summary>
public static class ConstructorNotaCredito
{
    public static ResultadoNotaCredito Construir(
        EmpresaEmisora emisor,
        EstablecimientoInfo establecimiento,
        NotaCreditoLeida leida,
        short ambiente,
        string? emailPruebas,
        IReadOnlyDictionary<string, string>? informacionAdicional = null,
        string moneda = "USD",
        short tipoEmision = 1)
    {
        var errores = new List<string>(leida.Errores);
        if (leida.Cliente is null)
        {
            errores.Add("La nota de crédito no tiene cliente.");
            return ResultadoNotaCredito.ConErrores(errores);
        }
        errores.AddRange(leida.Cliente.Errores);

        var cab = leida.Cabecera;
        var mod = leida.DocumentoModificado;

        if (cab.FechaEmision is null)
        {
            errores.Add("La nota de crédito no tiene fecha de emisión.");
        }

        var email = HelpersVenta.EmailDestino(leida.Cliente.Email, ambiente, emailPruebas);

        // La NC usa el punto de emisión DEL ESTABLECIMIENTO (a diferencia de la factura).
        var establecimientoDatil = new Establecimiento
        {
            Codigo = establecimiento.Codigo,
            PuntoEmision = establecimiento.PuntoEmision,
            Direccion = establecimiento.Direccion,
        };

        var nc = new NotaCredito
        {
            Secuencial = HelpersVenta.SecuencialNumerico(cab.Secuencial),
            Moneda = moneda,
            Ambiente = ambiente,
            TipoEmision = tipoEmision,
            FechaEmision = new DateTimeOffset(cab.FechaEmision ?? default, HelpersVenta.ZonaEcuador),
            FechaEmisionDocumentoModificado = new DateTimeOffset(mod.DateBill, HelpersVenta.ZonaEcuador),
            NumeroDocumentoModificado = mod.BillNumber,
            TipoDocumentoModificado = mod.BillCodeDoc,
            Motivo = mod.Cause,
            Emisor = HelpersVenta.Emisor(emisor, establecimientoDatil),
            Comprador = HelpersVenta.Comprador(leida.Cliente, email),
            Items = HelpersVenta.Items(leida.Lineas, cab.CodigoIva),
            Totales = new TotalesNotaCredito
            {
                TotalSinImpuestos = cab.TotalSinImpuestos,
                ImporteTotal = cab.TotalConImpuestos,
                Impuestos = HelpersVenta.AgruparImpuestos(leida.Lineas, cab.CodigoIva),
            },
            InformacionAdicional = informacionAdicional is { Count: > 0 }
                ? new Dictionary<string, string>(informacionAdicional)
                : null,
        };

        var guardar = new NotaCreditoParaGuardar(
            NumeroCompleto: cab.NumeroCompleto,
            Secuencial: cab.Secuencial,
            EstablecimientoId: establecimiento.EstablishmentId,
            Moneda: moneda,
            FechaEmision: cab.FechaEmision ?? default,
            Ambiente: ambiente,
            IssueType: tipoEmision,
            CodigoIva: cab.CodigoIva,
            CodigoPorcentajeIva: cab.CodigoPorcentajeIva,
            TotalSinImpuestos: cab.TotalSinImpuestos,
            BaseImponibleIva: cab.BaseImponibleIva,
            IvaValor: cab.IvaValor,
            TotalConImpuestos: cab.TotalConImpuestos,
            PostOrderPeach: leida.PostOrderPeach,
            Persona: HelpersVenta.Persona(leida.Cliente),
            Lineas: HelpersVenta.LineasParaGuardar(leida.Lineas, cab.CodigoIva),
            DocumentoModificado: new DocumentoModificadoParaGuardar(
                mod.DateBill, mod.BillNumber, mod.BillCodeDoc, mod.Cause));

        return new ResultadoNotaCredito
        {
            NotaCredito = nc,
            NumeroCompleto = cab.NumeroCompleto,
            EstablecimientoId = establecimiento.EstablishmentId,
            Guardar = guardar,
            Errores = errores,
        };
    }
}
