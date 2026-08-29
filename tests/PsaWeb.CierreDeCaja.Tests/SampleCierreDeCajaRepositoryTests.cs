using PsaWeb.Modules.CierreDeCaja.Data;

namespace PsaWeb.CierreDeCaja.Tests;

public class SampleCierreDeCajaRepositoryTests
{
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public async Task Devuelve_un_cierre_cuadrado_con_detalle()
    {
        var repo = new SampleCierreDeCajaRepository();

        var r = await repo.ObtenerAsync(Hoy, Hoy);

        Assert.Equal(3, r.Cobros.Count);
        Assert.Equal(r.Cobros.Sum(c => c.Subtotal), r.TotalCobros);
        Assert.Equal(0m, r.Diferencia);
        Assert.False(r.SinMovimientos);
    }

    [Fact]
    public async Task Rango_invertido_devuelve_sin_movimientos()
    {
        var repo = new SampleCierreDeCajaRepository();

        var r = await repo.ObtenerAsync(Hoy.AddDays(1), Hoy);

        Assert.True(r.SinMovimientos);
        Assert.Empty(r.Cobros);
    }
}
