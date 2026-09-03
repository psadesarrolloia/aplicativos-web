using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Comprobantes.Tests;

public class NumeroDocumentoSriTests
{
    [Fact]
    public void Estricto_acepta_el_formato_completo()
    {
        var n = NumeroDocumentoSri.AnalizarEstricto("001-002-000000123");

        Assert.True(n.EsValido);
        Assert.False(n.FueCorregido);
        Assert.Equal("001", n.CodigoEstablecimiento);
        Assert.Equal("002", n.PuntoEmision);
        Assert.Equal("000000123", n.Secuencial);
        Assert.Equal("001-002-000000123", n.Completo);
    }

    [Theory]
    [InlineData("0010020000000123")]      // 16, sin guiones
    [InlineData("1-2-3")]                  // muy corto
    [InlineData("001-002-00000012A")]      // no numérico
    [InlineData("0012-02-000000123")]      // guiones mal ubicados
    public void Estricto_rechaza_formatos_incorrectos(string numero)
    {
        Assert.False(NumeroDocumentoSri.AnalizarEstricto(numero).EsValido);
    }

    [Fact]
    public void Factura_corrige_secuencial_corto()
    {
        var n = NumeroDocumentoSri.AnalizarFactura("001-002-12345");

        Assert.True(n.EsValido);
        Assert.True(n.FueCorregido);
        Assert.Equal("000012345", n.Secuencial);
        Assert.Equal("001-002-000012345", n.Completo);
    }

    [Fact]
    public void Factura_acepta_el_formato_de_17_sin_corregir()
    {
        var n = NumeroDocumentoSri.AnalizarFactura("001-002-000000123");
        Assert.True(n.EsValido);
        Assert.False(n.FueCorregido);
    }

    [Fact]
    public void Factura_rechaza_secuencial_cero()
    {
        Assert.False(NumeroDocumentoSri.AnalizarFactura("001-002-0").EsValido);
    }
}

public class CodigosDocumentoTests
{
    [Theory]
    [InlineData("FACTURA", "01")]
    [InlineData("nota de venta", "02")]
    [InlineData("  Liquidacion ", "03")]
    public void Mapea_el_shipvia_al_codigo_sri(string shipVia, string codigo)
    {
        Assert.Equal(codigo, CodigosDocumento.CodigoDe(shipVia));
        Assert.True(CodigosDocumento.EsReconocido(shipVia));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("REMISION")]
    public void Tipo_no_reconocido_devuelve_null(string? shipVia)
    {
        Assert.Null(CodigosDocumento.CodigoDe(shipVia));
        Assert.False(CodigosDocumento.EsReconocido(shipVia));
    }
}
