using PsaWeb.Ats.Compras;
using PsaWeb.Ats.Esquema;

namespace PsaWeb.Ats.Tests;

public class InfoProveedorExteriorTests
{
    [Fact]
    public void DesdeJson_parsea_los_7_elementos_reales_de_un_proveedor_exterior()
    {
        // Caso real observado en CPTDC (sociedad, pago al exterior, régimen 01,
        // con convenio de doble tributación).
        var info = InfoProveedorExterior.DesdeJson("[\"02\",\"02\",\"01\",\"331\",\"331\",\"SI\",\"NA\"]");

        Assert.Equal(TipoProveedorExterior.Sociedad, info.TipoProveedor);
        Assert.Equal(TipoPagoExterior.Exterior, info.TipoPago);
        Assert.Equal("01", info.RegimenTipo);
        Assert.Equal("331", info.PaisPagoGeneral);
        Assert.Equal("331", info.PaisPago);
        Assert.Equal(RespuestaSiNo.Si, info.ConvenioDobleTributacion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("\"02\"")] // no es un array
    [InlineData("[\"01\",\"01\"]")] // menos de 7 elementos
    public void DesdeJson_texto_invalido_devuelve_Ninguna(string json)
    {
        var info = InfoProveedorExterior.DesdeJson(json);
        Assert.Equal(InfoProveedorExterior.Ninguna, info);
    }

    [Fact]
    public void ArmarPagoExterior_sin_info_deja_todo_en_NA()
    {
        var pago = InfoProveedorExterior.ArmarPagoExterior(InfoProveedorExterior.Ninguna);

        Assert.Equal(pagoLocExtType.Item01, pago.pagoLocExt);
        Assert.Equal("NA", pago.paisEfecPago);
        Assert.Equal(aplicConvDobTribType.NA, pago.aplicConvDobTrib);
        Assert.Equal(aplicConvDobTribType.NA, pago.pagExtSujRetNorLeg);
    }

    [Fact]
    public void ArmarPagoExterior_pago_local_ignora_el_resto_del_JSON()
    {
        // Caso real: paymentType=local (01) con dobleTax/pagExtSujRetNorLeg en
        // el JSON ("NO"/"SI") que el `.exe` igual ignora en esta rama.
        var info = InfoProveedorExterior.DesdeJson("[\"02\",\"01\",\"01\",\"NA\",\"NA\",\"NO\",\"SI\"]");
        var pago = InfoProveedorExterior.ArmarPagoExterior(info);

        Assert.Equal(pagoLocExtType.Item01, pago.pagoLocExt);
        Assert.Equal("NA", pago.paisEfecPago);
        Assert.Equal(aplicConvDobTribType.NA, pago.aplicConvDobTrib);
        Assert.Equal(aplicConvDobTribType.NA, pago.pagExtSujRetNorLeg);
    }

    [Theory]
    [InlineData(RespuestaSiNo.Si)]
    [InlineData(RespuestaSiNo.No)]
    [InlineData(RespuestaSiNo.Ninguna)]
    public void ArmarPagoExterior_pago_exterior_Bug_B3_pagExtSujRetNorLeg_siempre_es_SI(RespuestaSiNo pagoSujetoValorDelJson)
    {
        // Bug B3 (LoadVendor): el `.exe` compara pagoExterior.pagExtSujRetNorLeg
        // contra sí mismo antes de asignarlo, así que siempre lee el default
        // del enum (SI) y el resultado final es SIEMPRE "SI" — sin importar lo
        // que diga el JSON (7º elemento). Se prueba con los 3 valores posibles.
        var info = new InfoProveedorExterior(
            TipoProveedorExterior.Sociedad, TipoPagoExterior.Exterior, "01", "331", "331",
            RespuestaSiNo.Si, pagoSujetoValorDelJson);

        var pago = InfoProveedorExterior.ArmarPagoExterior(info);

        Assert.Equal(aplicConvDobTribType.SI, pago.pagExtSujRetNorLeg);
    }

    [Theory]
    [InlineData("01")]
    [InlineData("02")]
    [InlineData("03")]
    public void ArmarPagoExterior_pago_exterior_arma_segun_el_regimen(string regimen)
    {
        var info = new InfoProveedorExterior(
            TipoProveedorExterior.Sociedad, TipoPagoExterior.Exterior, regimen, "110", "110",
            RespuestaSiNo.Si, RespuestaSiNo.Ninguna);

        var pago = InfoProveedorExterior.ArmarPagoExterior(info);

        Assert.Equal(pagoLocExtType.Item02, pago.pagoLocExt);
        Assert.Equal("110", pago.paisEfecPago);
        Assert.Equal(aplicConvDobTribType.SI, pago.aplicConvDobTrib);
        switch (regimen)
        {
            case "01":
                Assert.Equal(tipoRegiType.Item01, pago.tipoRegi);
                Assert.Equal("110", pago.paisEfecPagoGen);
                break;
            case "02":
                Assert.Equal(tipoRegiType.Item02, pago.tipoRegi);
                Assert.Equal("110", pago.paisEfecPagoParFis);
                break;
            case "03":
                Assert.Equal(tipoRegiType.Item03, pago.tipoRegi);
                Assert.Equal(string.Empty, pago.denopagoRegFis);
                break;
        }
    }
}
