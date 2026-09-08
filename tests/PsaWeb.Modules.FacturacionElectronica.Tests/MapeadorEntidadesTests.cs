using PsaWeb.Comprobantes.Venta;
using PsaWeb.Modules.FacturacionElectronica.Data;

namespace PsaWeb.Modules.FacturacionElectronica.Tests;

public class MapeadorEntidadesTests
{
    private static PersonaParaGuardar Persona() =>
        new("1790011110001", "04", "CLIENTE S.A.", "Quito", "cli@ejemplo.com", "022345678", "022999888");

    private static LineaFacturaParaGuardar Linea() =>
        new("Servicio", "ART-1", null, 1, 100, 0, 100, "2", "2", 100, 12, 0.12);

    [Fact]
    public void DesdeFactura_mapea_cabecera_persona_y_detalles()
    {
        var g = new FacturaParaGuardar(
            CodDoc: "01", NumeroCompleto: "001-003-000013538", Secuencial: "000013538",
            EstablecimientoId: 35, Moneda: "USD", FechaEmision: new DateTime(2026, 9, 4),
            FechaVencimiento: null, Ambiente: 1, IssueType: 1, TotalDescuento: 0,
            CodigoIva: "2", CodigoPorcentajeIva: "2", TotalSinImpuestos: 100, BaseImponibleIva: 100,
            IvaValor: 12, TotalConImpuestos: 112, PostOrderPeach: "9001",
            Persona: Persona(), Lineas: new[] { Linea() });

        var (factura, persona, detalles) = MapeadorEntidades.DesdeFactura(g, "1791313747001");

        Assert.Equal("01", factura.CodDoc);
        Assert.Equal("001-003-000013538", factura.FacturaNumberComplete);
        Assert.Equal("000013538", factura.FacturaNumber);
        Assert.Equal(35, factura.TransmitterEstablishment);
        Assert.Equal("1791313747001", factura.TransmitterRuc);
        Assert.Equal((short)1, factura.Ambient);
        Assert.Equal(112, factura.TotalAmount);
        Assert.Equal(100, factura.TotalWithoutTax);
        Assert.Equal(1, factura.TransType);
        Assert.True(factura.IsValid);
        Assert.Null(factura.DateDue);

        Assert.Equal("1790011110001", persona.PersonId);
        Assert.Equal("CLIENTE S.A.", persona.Name);
        Assert.Equal("022999888", persona.FaxNum);

        var d = Assert.Single(detalles);
        Assert.Equal("ART-1", d.MainCode);
        Assert.Null(d.AuxCode);
        Assert.Equal(0.12, d.IVAPercent);
        Assert.Equal(12, d.IVAValue);
    }

    [Fact]
    public void DesdeNotaCredito_arma_NcDetail_y_TransType_1()
    {
        var g = new NotaCreditoParaGuardar(
            NumeroCompleto: "001-003-000000045", Secuencial: "000000045", EstablecimientoId: 35,
            Moneda: "USD", FechaEmision: new DateTime(2026, 9, 5), Ambiente: 2, IssueType: 1,
            CodigoIva: "2", CodigoPorcentajeIva: "2", TotalSinImpuestos: 100, BaseImponibleIva: 100,
            IvaValor: 12, TotalConImpuestos: 112, PostOrderPeach: "9100",
            Persona: Persona(), Lineas: new[] { Linea() },
            DocumentoModificado: new DocumentoModificadoParaGuardar(
                new DateTime(2026, 8, 20), "001-003-000013538", "01", "Devolución"));

        var (nota, _, _, detalleNc) = MapeadorEntidades.DesdeNotaCredito(g, "1791313747001");

        Assert.Equal("04", nota.CodDoc);
        Assert.Equal(1, nota.TransType);
        Assert.Null(nota.DateDue);
        Assert.Equal("001-003-000013538", detalleNc.BillNumber);
        Assert.Equal("01", detalleNc.BillCodeDoc);
        Assert.Equal("Devolución", detalleNc.Cause);
        Assert.Equal(new DateTime(2026, 8, 20), detalleNc.DateBill);
    }

    [Fact]
    public void DesdeLiquidacion_es_codDoc_03_TransType_2()
    {
        var g = new LiquidacionParaGuardar(
            NumeroCompleto: "001-001-000000123", Secuencial: "000000123", EstablecimientoId: 35,
            Moneda: "USD", FechaEmision: new DateTime(2026, 9, 3), FechaVencimiento: new DateTime(2026, 10, 3),
            Ambiente: 1, IssueType: 1, CodigoIva: "2", CodigoPorcentajeIva: "4",
            TotalSinImpuestos: 100, BaseImponibleIva: 100, IvaValor: 15, TotalConImpuestos: 115,
            PostOrderPeach: "9200", Proveedor: Persona(), Lineas: new[] { Linea() });

        var (factura, _, _) = MapeadorEntidades.DesdeLiquidacion(g, "1791313747001");

        Assert.Equal("03", factura.CodDoc);
        Assert.Equal(2, factura.TransType);
        Assert.Equal("4", factura.PercentIVACode);
        Assert.Equal(new DateTime(2026, 10, 3), factura.DateDue);
        Assert.Equal(115, factura.TotalAmount);
    }
}
