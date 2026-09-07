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

    [Fact]
    public async Task ObtenerParaRuc_del_sample_ignora_el_ruc()
    {
        var repo = new SampleCierreDeCajaRepository();

        var porRuc = await repo.ObtenerParaRucAsync("1790000000001", Hoy, Hoy);
        var normal = await repo.ObtenerAsync(Hoy, Hoy);

        Assert.Equal(normal.TotalCobros, porRuc.TotalCobros);
        Assert.Equal(normal.Cobros.Count, porRuc.Cobros.Count);
    }

    [Fact]
    public async Task ResolverSinShell_no_tiene_empresa_y_no_fuerza_cadena()
    {
        var r = new SinShellResolverEmpresaSage();

        Assert.Null(r.RucSesion);
        Assert.Null(await r.CadenaOdbcAsync("1790000000001"));
        r.Cambio += () => { }; // el evento no-op no revienta
    }
}
