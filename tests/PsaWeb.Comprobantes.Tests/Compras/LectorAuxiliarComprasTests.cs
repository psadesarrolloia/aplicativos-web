using PsaWeb.Comprobantes.Compras;

namespace PsaWeb.Comprobantes.Tests.Compras;

public class LectorAuxiliarComprasTests
{
    [Theory]
    [InlineData("AUTORETENCION")]
    [InlineData("autoretencion")]
    [InlineData(" AUTORETENCION ")]
    public void EsAutoretencion_reconoce_la_palabra_sin_distinguir_mayusculas_ni_espacios(string shipVia)
    {
        Assert.True(LectorAuxiliarCompras.EsAutoretencion(shipVia));
    }

    [Theory]
    [InlineData("FACTURA")]
    [InlineData("LIQUIDACION")]
    [InlineData("")]
    [InlineData(null)]
    public void EsAutoretencion_es_false_para_cualquier_otro_valor(string? shipVia)
    {
        Assert.False(LectorAuxiliarCompras.EsAutoretencion(shipVia));
    }
}
