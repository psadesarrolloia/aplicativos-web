using PsaWeb.Ats.Compras;
using PsaWeb.Ats.Compras.Muestra;
using PsaWeb.Ats.Esquema;

namespace PsaWeb.Ats.Tests;

public class ArmadorComprasAtsTests
{
    private static ProveedorAts ProveedorNacional(string id = "1790011110001") =>
        new(true, TiposIdentificacionProveedorAts.Ruc, id, "PROVEEDOR DEMO S.A.", string.Empty,
            InfoProveedorExterior.ArmarPagoExterior(InfoProveedorExterior.Ninguna), null);

    private static CompraCruda Base(string tipoComprobante, ProveedorAts? proveedor = null) => new(
        TipoComprobante: tipoComprobante,
        CodSustento: CodigosSustentoAts.Credito,
        Establecimiento: "001",
        PuntoEmision: "001",
        Secuencial: "000000012",
        FechaRegistro: "05/09/2026",
        FechaEmision: "05/09/2026",
        Autorizacion: "auth-123",
        Proveedor: proveedor ?? ProveedorNacional(),
        Buckets: BucketsComprasAts.Cero,
        ShipToAddress2: string.Empty,
        ShipToCity: string.Empty,
        RetencionesRenta: null,
        NumeroCompletoCompraOriginal: null,
        AutorizacionCompraOriginal: null);

    [Fact]
    public void Armar_siempre_emite_valorRetencionNc_en_cero_especificado()
    {
        // Hallazgo 4 de docs/PLAN-APP3-ATS.md §1.4: campo nuevo del SRI (2020)
        // que el `.exe` no calcula, pero el DIMM lo trae en 0.00 en cada
        // compra real — se emite igual para que el esquema quede completo.
        var detalle = ArmadorComprasAts.Armar(Base(TiposComprobanteComprasAts.Factura));

        Assert.Equal(0m, detalle.valorRetencionNc);
        Assert.True(detalle.valorRetencionNcSpecified);
    }

    [Fact]
    public void Armar_parteRel_siempre_NO()
    {
        var detalle = ArmadorComprasAts.Armar(Base(TiposComprobanteComprasAts.Factura));

        Assert.Equal(parteRelType.NO, detalle.parteRel);
    }

    [Fact]
    public void Proveedor_no_encontrado_no_asigna_tpIdProv_ni_pagoExterior()
    {
        // Port fiel: si LoadVendor.CorrectLoaded() es false (0 filas), el
        // `.exe` no toca tpIdProv/idProv/pagoExterior — quedan sin asignar.
        var detalle = ArmadorComprasAts.Armar(Base(TiposComprobanteComprasAts.Factura, ProveedorAts.NoEncontrado));

        Assert.Null(detalle.tpIdProv);
        Assert.Null(detalle.pagoExterior);
    }

    [Fact]
    public void Proveedor_exterior_expone_tipoProv_y_denoProv()
    {
        var proveedor = new ProveedorAts(true, TiposIdentificacionProveedorAts.Exterior, "PA1234567",
            "PROVEEDOR EXTERIOR", "02", InfoProveedorExterior.ArmarPagoExterior(InfoProveedorExterior.Ninguna), null);

        var detalle = ArmadorComprasAts.Armar(Base(TiposComprobanteComprasAts.Factura, proveedor));

        Assert.Equal("02", detalle.tipoProv);
        Assert.Equal("PROVEEDOR EXTERIOR", detalle.denoProv);
    }

    [Fact]
    public void Proveedor_nacional_no_expone_tipoProv_ni_denoProv()
    {
        var detalle = ArmadorComprasAts.Armar(Base(TiposComprobanteComprasAts.Factura));

        Assert.Null(detalle.tipoProv);
        Assert.Null(detalle.denoProv);
    }

    [Fact]
    public void TipoRegiSpecified_solo_cuando_el_pago_es_al_exterior()
    {
        var proveedor = new ProveedorAts(true, TiposIdentificacionProveedorAts.Exterior, "PA1234567", "X", "02",
            InfoProveedorExterior.ArmarPagoExterior(new InfoProveedorExterior(
                TipoProveedorExterior.Sociedad, TipoPagoExterior.Exterior, "01", "331", "331", RespuestaSiNo.Si, RespuestaSiNo.Ninguna)),
            null);

        var detalle = ArmadorComprasAts.Armar(Base(TiposComprobanteComprasAts.Factura, proveedor));

        Assert.True(detalle.pagoExterior.tipoRegiSpecified);
    }

    [Theory]
    [InlineData(499.99, false)]
    [InlineData(500.00, true)]
    [InlineData(500.01, true)]
    public void FormaDePago_20_desde_500_inclusive(decimal totalCompra, bool esperaFormaDePago)
    {
        var crudo = Base(TiposComprobanteComprasAts.Factura) with
        {
            Buckets = BucketsComprasAts.Cero with { BaseImpGrav = totalCompra },
        };

        var detalle = ArmadorComprasAts.Armar(crudo);

        if (esperaFormaDePago)
        {
            Assert.Equal(new[] { "20" }, detalle.formasDePago);
        }
        else
        {
            Assert.Null(detalle.formasDePago);
        }
    }

    [Fact]
    public void NotaCredito_no_lleva_forma_de_pago_ni_air()
    {
        var crudo = Base(TiposComprobanteComprasAts.NotaCredito) with
        {
            Buckets = BucketsComprasAts.Cero with { BaseImpGrav = 1000m },
            RetencionesRenta = new[] { new detalleAirComprasType { codRetAir = "310" } },
        };

        var detalle = ArmadorComprasAts.Armar(crudo);

        Assert.Null(detalle.formasDePago);
        Assert.Null(detalle.air); // el `air` cargado no se usa para NC.
    }

    [Fact]
    public void NotaCredito_con_compra_original_arma_docModificado()
    {
        var crudo = Base(TiposComprobanteComprasAts.NotaCredito) with
        {
            NumeroCompletoCompraOriginal = "003-001-000011279",
            AutorizacionCompraOriginal = "auth-original",
        };

        var detalle = ArmadorComprasAts.Armar(crudo);

        Assert.Equal("01", detalle.docModificado);
        Assert.Equal("003", detalle.estabModificado);
        Assert.Equal("001", detalle.ptoEmiModificado);
        Assert.Equal("000011279", detalle.secModificado);
        Assert.Equal("auth-original", detalle.autModificado);
    }

    [Fact]
    public void NotaCredito_sin_compra_original_no_arma_docModificado()
    {
        var detalle = ArmadorComprasAts.Armar(Base(TiposComprobanteComprasAts.NotaCredito));

        Assert.Null(detalle.docModificado);
    }

    [Fact]
    public void Retencion_recibida_por_estabRetencion1_se_arma_desde_ShipToAddress2_y_ShipToCity()
    {
        var crudo = Base(TiposComprobanteComprasAts.Factura) with
        {
            ShipToAddress2 = "001-001-000000045",
            ShipToCity = "9999999999",
        };

        var detalle = ArmadorComprasAts.Armar(crudo);

        Assert.Equal("001", detalle.estabRetencion1);
        Assert.Equal("001", detalle.ptoEmiRetencion1);
        Assert.Equal("000000045", detalle.secRetencion1);
        Assert.Equal("9999999999", detalle.autRetencion1);
        Assert.Equal(detalle.fechaEmision, detalle.fechaEmiRet1);
    }

    [Fact]
    public void Retencion_recibida_no_se_arma_si_ShipToAddress2_es_corto()
    {
        var crudo = Base(TiposComprobanteComprasAts.Factura) with
        {
            ShipToAddress2 = "corto",
            ShipToCity = "9999999999",
        };

        var detalle = ArmadorComprasAts.Armar(crudo);

        Assert.Null(detalle.estabRetencion1);
    }

    [Fact]
    public void Los_datos_de_muestra_se_arman_sin_reventar()
    {
        var filas = ComprasMuestraAts.Filas();

        Assert.Equal(3, filas.Count);
        Assert.Contains(filas, f => f.tipoComprobante == TiposComprobanteComprasAts.Factura);
        Assert.Contains(filas, f => f.tipoComprobante == TiposComprobanteComprasAts.NotaCredito);
        Assert.Contains(filas, f => f.tipoComprobante == TiposComprobanteComprasAts.Liquidacion);
    }
}
