using PsaWeb.Ats.Anulados;

namespace PsaWeb.Ats.Tests;

public class ClasificadorAnuladosAtsTests
{
    private static readonly IReadOnlyList<FilaJrnlHdrAts> SinOrdenesDeCompra = Array.Empty<FilaJrnlHdrAts>();
    private static readonly IReadOnlyDictionary<long, string> SinAutorizaciones = new Dictionary<long, string>();

    private static FilaJrnlHdrAts Fila(
        long postOrder = 1, string reference = "001-001-000003611", long custVendId = 2,
        int journalEx = 8, int jrnlKeyJournal = 3, string invPosoOrderNumber = "",
        string shipVia = "", string termsDescription = "", string shipToAddress2 = "") =>
        new(postOrder, reference, custVendId, journalEx, jrnlKeyJournal, invPosoOrderNumber, shipVia, termsDescription, shipToAddress2);

    [Fact]
    public void Venta_factura_arma_desde_la_propia_Reference()
    {
        // Caso real de CPTDC julio/2026: sin línea AUT-SRI para una factura de
        // venta cancelada -> cae en el relleno "9999999999".
        var fila = Fila(postOrder: 101894, reference: "001-001-000003611", journalEx: 8, jrnlKeyJournal: 3);

        var resultado = ClasificadorAnuladosAts.Clasificar(fila, SinOrdenesDeCompra, SinAutorizaciones);

        var item = Assert.Single(resultado);
        Assert.Equal("18", item.tipoComprobante);
        Assert.Equal("001", item.establecimiento);
        Assert.Equal("001", item.puntoEmision);
        Assert.Equal("000003611", item.secuencialInicio);
        Assert.Equal(item.secuencialInicio, item.secuencialFin);
        Assert.Equal("9999999999", item.autorizacion);
    }

    [Fact]
    public void Venta_nota_de_credito_tipo_04()
    {
        var fila = Fila(journalEx: 9, jrnlKeyJournal: 3);

        var item = Assert.Single(ClasificadorAnuladosAts.Clasificar(fila, SinOrdenesDeCompra, SinAutorizaciones));

        Assert.Equal("04", item.tipoComprobante);
    }

    [Fact]
    public void Autorizacion_encontrada_no_usa_el_relleno()
    {
        var fila = Fila(postOrder: 55);
        var autorizaciones = new Dictionary<long, string> { [55] = "auth-real" };

        var item = Assert.Single(ClasificadorAnuladosAts.Clasificar(fila, SinOrdenesDeCompra, autorizaciones));

        Assert.Equal("auth-real", item.autorizacion);
    }

    [Fact]
    public void Diario_Sales_con_subtipo_no_reconocido_no_arma_nada()
    {
        // Combinación inconsistente (JrnlKeyJournal=3 pero JournalEx=11): el
        // switch de Sales no tiene ese caso, el `.exe` no arma nada.
        var fila = Fila(journalEx: 11, jrnlKeyJournal: 3);

        Assert.Empty(ClasificadorAnuladosAts.Clasificar(fila, SinOrdenesDeCompra, SinAutorizaciones));
    }

    [Fact]
    public void Compra_sin_INV_POSOOrderNumber_no_arma_nada()
    {
        var fila = Fila(journalEx: 11, jrnlKeyJournal: 4, invPosoOrderNumber: "");

        Assert.Empty(ClasificadorAnuladosAts.Clasificar(fila, SinOrdenesDeCompra, SinAutorizaciones));
    }

    [Fact]
    public void Compra_sin_orden_original_encontrada_no_arma_nada()
    {
        var fila = Fila(journalEx: 11, jrnlKeyJournal: 4, invPosoOrderNumber: "OC-1", custVendId: 5);

        Assert.Empty(ClasificadorAnuladosAts.Clasificar(fila, SinOrdenesDeCompra, SinAutorizaciones));
    }

    [Fact]
    public void Compra_liquidacion_y_retencion_pueden_armarse_ambas_del_mismo_documento()
    {
        var fila = Fila(journalEx: 11, jrnlKeyJournal: 4, invPosoOrderNumber: "OC-1", custVendId: 5);
        var ordenOriginal = new FilaJrnlHdrAts(
            PostOrder: 900, Reference: "OC-1", CustVendId: 5, JournalEx: 0, JrnlKeyJournal: 0,
            InvPosoOrderNumber: "", ShipVia: "LIQUIDACION",
            TermsDescription: "001-002-000000045", ShipToAddress2: "001-003-000000078");

        var resultado = ClasificadorAnuladosAts.Clasificar(fila, new[] { ordenOriginal }, SinAutorizaciones);

        Assert.Equal(2, resultado.Count);
        var liq = Assert.Single(resultado, r => r.tipoComprobante == "03");
        Assert.Equal("001", liq.establecimiento);
        Assert.Equal("002", liq.puntoEmision);
        Assert.Equal("000000045", liq.secuencialInicio);
        var ret = Assert.Single(resultado, r => r.tipoComprobante == "07");
        Assert.Equal("001", ret.establecimiento);
        Assert.Equal("003", ret.puntoEmision);
        Assert.Equal("000000078", ret.secuencialInicio);
    }

    [Fact]
    public void Compra_sin_liquidacion_ni_retencion_no_arma_nada()
    {
        var fila = Fila(journalEx: 11, jrnlKeyJournal: 4, invPosoOrderNumber: "OC-1", custVendId: 5);
        var ordenOriginal = new FilaJrnlHdrAts(
            PostOrder: 900, Reference: "OC-1", CustVendId: 5, JournalEx: 0, JrnlKeyJournal: 0,
            InvPosoOrderNumber: "", ShipVia: "FACTURA", TermsDescription: "", ShipToAddress2: "");

        Assert.Empty(ClasificadorAnuladosAts.Clasificar(fila, new[] { ordenOriginal }, SinAutorizaciones));
    }

    [Fact]
    public void OrdenDeCompra_referenciada_por_otro_documento_no_se_reporta()
    {
        var fila = Fila(postOrder: 700, journalEx: 18, jrnlKeyJournal: 10, shipVia: "LIQUIDACION", termsDescription: "001-001-000000099");
        // Otra orden cuyo INV_POSOOrderNumber apunta al PostOrder de `fila`.
        var otra = new FilaJrnlHdrAts(800, "x", 1, 0, 0, InvPosoOrderNumber: "700", ShipVia: "", TermsDescription: "", ShipToAddress2: "");

        Assert.Empty(ClasificadorAnuladosAts.Clasificar(fila, new[] { otra }, SinAutorizaciones));
    }

    [Fact]
    public void OrdenDeCompra_no_referenciada_liquidacion_sin_autorizacion_no_usa_relleno()
    {
        // Asimetría real del `.exe`: esta rama en particular NO tiene el
        // relleno "9999999999" (a diferencia de las otras 3 ramas similares) —
        // si no hay AUT-SRI, el campo queda vacío/null.
        var fila = Fila(postOrder: 700, journalEx: 18, jrnlKeyJournal: 10, shipVia: "LIQUIDACION", termsDescription: "001-001-000000099");

        var resultado = ClasificadorAnuladosAts.Clasificar(fila, SinOrdenesDeCompra, SinAutorizaciones);

        var liq = Assert.Single(resultado, r => r.tipoComprobante == "03");
        Assert.Null(liq.autorizacion);
    }

    [Fact]
    public void OrdenDeCompra_no_referenciada_arma_retencion_desde_ShipToAddress2_con_relleno_de_autorizacion()
    {
        var fila = Fila(postOrder: 700, journalEx: 18, jrnlKeyJournal: 10, shipToAddress2: "001-001-000000099");

        var resultado = ClasificadorAnuladosAts.Clasificar(fila, SinOrdenesDeCompra, SinAutorizaciones);

        var ret = Assert.Single(resultado);
        Assert.Equal("07", ret.tipoComprobante);
        Assert.Equal("001", ret.establecimiento);
        Assert.Equal("9999999999", ret.autorizacion); // esta sí pasa por la cola común, con relleno.
    }

    [Fact]
    public void OrdenDeCompra_no_referenciada_con_ShipToAddress2_corto_no_revienta()
    {
        // El `.exe` no se cuida acá y tiraría ArgumentOutOfRangeException;
        // el puerto prefiere omitir la fila.
        var fila = Fila(postOrder: 700, journalEx: 18, jrnlKeyJournal: 10, shipToAddress2: "corto");

        Assert.Empty(ClasificadorAnuladosAts.Clasificar(fila, SinOrdenesDeCompra, SinAutorizaciones));
    }
}
