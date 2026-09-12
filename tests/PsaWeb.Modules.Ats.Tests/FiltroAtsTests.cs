using PsaWeb.Modules.Ats.Data;

namespace PsaWeb.Modules.Ats.Tests;

public class FiltroAtsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void Mes_fuera_de_rango_es_invalido(int mes)
    {
        var filtro = new FiltroAts(DateTime.Today.Year, mes);
        Assert.False(filtro.MesValido);
        Assert.False(filtro.Valido);
    }

    [Fact]
    public void Anio_futuro_es_invalido()
    {
        var filtro = new FiltroAts(DateTime.Today.Year + 1, 1);
        Assert.False(filtro.AnioValido);
        Assert.False(filtro.Valido);
    }

    [Fact]
    public void Anio_actual_y_mes_valido_es_valido()
    {
        var filtro = new FiltroAts(DateTime.Today.Year, 7);
        Assert.True(filtro.Valido);
    }
}
