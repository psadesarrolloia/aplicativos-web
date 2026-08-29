using PsaWeb.Modules.CierreDeCaja.Data;

namespace PsaWeb.CierreDeCaja.Tests;

public class ResultadoCierreTests
{
    [Theory]
    [InlineData(100, 100, 0)]      // cuadra
    [InlineData(150, 100, 50)]     // sobran ventas
    [InlineData(80, 100, -20)]     // sobran cobros
    public void Diferencia_es_ventas_menos_cobros(decimal ventas, decimal cobros, decimal esperado)
    {
        var r = new ResultadoCierre(Array.Empty<CobroPorTipo>(), TotalCobros: cobros, TotalVentas: ventas);

        Assert.Equal(esperado, r.Diferencia);
    }

    [Fact]
    public void SinMovimientos_true_cuando_no_hay_cobros_ni_ventas()
    {
        var r = new ResultadoCierre(Array.Empty<CobroPorTipo>(), 0m, 0m);

        Assert.True(r.SinMovimientos);
    }

    [Fact]
    public void SinMovimientos_false_cuando_hay_ventas_sin_cobros()
    {
        var r = new ResultadoCierre(Array.Empty<CobroPorTipo>(), 0m, 500m);

        Assert.False(r.SinMovimientos);
    }
}
