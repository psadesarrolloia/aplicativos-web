using PsaWeb.Comprobantes.Venta;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.FacturacionElectronica.Data;

/// <summary>
/// Convierte los DTOs planos de <c>PsaWeb.Comprobantes</c> (<c>*ParaGuardar</c>) en
/// las entidades EF de PeachEBills que persiste <c>RepositorioComprobantesVenta</c>.
/// Mantiene la capa de negocio (<c>PsaWeb.Comprobantes</c>) sin dependencia de EF.
/// </summary>
internal static class MapeadorEntidades
{
    public static (Facturas factura, Persons persona, List<Details> detalles) DesdeFactura(
        FacturaParaGuardar g, string ruc)
    {
        var factura = Cabecera(g.CodDoc, transType: 1, ruc, g.NumeroCompleto, g.Secuencial,
            g.EstablecimientoId, g.Moneda, g.FechaEmision, g.FechaVencimiento, g.Ambiente, g.IssueType,
            g.TotalDescuento, g.CodigoIva, g.CodigoPorcentajeIva, g.TotalSinImpuestos, g.BaseImponibleIva,
            g.IvaValor, g.TotalConImpuestos, g.PostOrderPeach);
        return (factura, Persona(g.Persona), Detalles(g.Lineas));
    }

    public static (Facturas nota, Persons persona, List<Details> detalles, NcDetail detalleNc) DesdeNotaCredito(
        NotaCreditoParaGuardar g, string ruc)
    {
        var nota = Cabecera(g.CodDoc, transType: 1, ruc, g.NumeroCompleto, g.Secuencial,
            g.EstablecimientoId, g.Moneda, g.FechaEmision, fechaVencimiento: null, g.Ambiente, g.IssueType,
            totalDescuento: 0, g.CodigoIva, g.CodigoPorcentajeIva, g.TotalSinImpuestos, g.BaseImponibleIva,
            g.IvaValor, g.TotalConImpuestos, g.PostOrderPeach);

        var d = g.DocumentoModificado;
        var detalleNc = new NcDetail
        {
            DateBill = d.DateBill,
            BillNumber = d.BillNumber,
            BillCodeDoc = d.BillCodeDoc,
            Cause = d.Cause,
        };
        return (nota, Persona(g.Persona), Detalles(g.Lineas), detalleNc);
    }

    public static (Facturas factura, Persons persona, List<Details> detalles) DesdeLiquidacion(
        LiquidacionParaGuardar g, string ruc)
    {
        var factura = Cabecera(g.CodDoc, g.TransType, ruc, g.NumeroCompleto, g.Secuencial,
            g.EstablecimientoId, g.Moneda, g.FechaEmision, g.FechaVencimiento, g.Ambiente, g.IssueType,
            totalDescuento: 0, g.CodigoIva, g.CodigoPorcentajeIva, g.TotalSinImpuestos, g.BaseImponibleIva,
            g.IvaValor, g.TotalConImpuestos, g.PostOrderPeach);
        return (factura, Persona(g.Proveedor), Detalles(g.Lineas));
    }

    private static Facturas Cabecera(
        string codDoc, int transType, string ruc, string numeroCompleto, string secuencial,
        int establecimientoId, string moneda, DateTime fechaEmision, DateTime? fechaVencimiento,
        short ambiente, short issueType, double totalDescuento, string codigoIva, string codigoPorcentajeIva,
        double totalSinImpuestos, double baseImponibleIva, double ivaValor, double totalConImpuestos,
        string postOrderPeach) => new()
    {
        CodDoc = codDoc,
        FacturaNumberComplete = numeroCompleto,
        FacturaNumber = secuencial,
        TransmitterEstablishment = establecimientoId,
        TransmitterRuc = ruc,
        CurrencyIsoId = moneda,
        DateIssued = fechaEmision,
        Ambient = ambiente,
        Comprador = string.Empty, // lo fija el repositorio con la identificación de la persona
        IssueType = issueType,
        TotalBillTip = 0,
        TotalDiscount = totalDescuento,
        IVACode = codigoIva,
        PercentIVACode = codigoPorcentajeIva,
        TotalWithoutTax = totalSinImpuestos,
        TotalAmountForIVA = baseImponibleIva,
        IVAValue = ivaValor,
        TotalAmount = totalConImpuestos,
        PostOrderPeach = postOrderPeach,
        IsValid = true,
        DateDue = fechaVencimiento,
        TransType = transType,
        TwhRequired = false,
    };

    private static Persons Persona(PersonaParaGuardar p) => new()
    {
        PersonId = p.Identificacion,
        Name = p.RazonSocial,
        Type = p.TipoIdentificacion,
        Email = p.Email,
        Phone = p.Telefono,
        Address = p.Direccion,
        FaxNum = p.Fax,
    };

    private static List<Details> Detalles(IReadOnlyList<LineaFacturaParaGuardar> lineas) =>
        lineas.Select(l => new Details
        {
            Description = l.Descripcion,
            MainCode = l.CodigoPrincipal,
            AuxCode = l.CodigoAuxiliar,
            Quantity = l.Cantidad,
            UnitPrice = l.PrecioUnitario,
            Discount = l.Descuento,
            AmountWithoutTAX = l.SubtotalSinImpuestos,
            IVACode = l.CodigoIva,
            PercentIVACode = l.CodigoPorcentajeIva,
            AmountForIVA = l.BaseImponibleIva,
            IVAValue = l.IvaValor,
            IVAPercent = l.IvaPorcentaje,
        }).ToList();
}
