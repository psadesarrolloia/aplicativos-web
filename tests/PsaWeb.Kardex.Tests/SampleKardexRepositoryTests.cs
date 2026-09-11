using PsaWeb.Modules.Kardex.Data;

namespace PsaWeb.Kardex.Tests;

public class SampleKardexRepositoryTests
{
    private static readonly DateOnly Desde = new(2026, 9, 1);
    private static readonly DateOnly Hasta = new(2026, 9, 30);

    private static readonly IKardexRepository Repo = new SampleKardexRepository();

    private static FiltroKardex Filtro(
        IReadOnlyList<string>? items = null, string? cuenta = null,
        string? itemDesde = null, string? itemHasta = null,
        DateOnly? desde = null, DateOnly? hasta = null, bool incluirVacios = true)
        => new(desde ?? Desde, hasta ?? Hasta,
               items ?? Array.Empty<string>(),
               cuenta is null ? Array.Empty<string>() : new[] { cuenta },
               itemDesde, itemHasta, incluirVacios);

    [Fact]
    public async Task Lista_items_y_cuentas()
    {
        Assert.NotEmpty(await Repo.ItemsAsync());
        Assert.Contains(await Repo.CuentasAsync(), c => c.Id == "13101");
    }

    [Fact]
    public async Task Sin_acotador_devuelve_vacio()
    {
        var r = await Repo.GenerarAsync(Filtro());
        Assert.True(r.SinMovimientos);
    }

    [Fact]
    public async Task Rango_invertido_devuelve_vacio()
    {
        var r = await Repo.GenerarAsync(Filtro(items: new[] { "CS-012" }, desde: Hasta, hasta: Desde));
        Assert.True(r.SinMovimientos);
    }

    [Fact]
    public async Task Por_item_devuelve_inicial_mas_movimientos()
    {
        var r = await Repo.GenerarAsync(Filtro(items: new[] { "CS-012" }));

        Assert.All(r.Filas, f => Assert.Equal("CS-012", f.ItemId));
        var inicial = Assert.Single(r.Filas, f => f.EsInicial);
        Assert.Equal(".INICIAL.", inicial.Referencia);
        Assert.Equal(Desde, inicial.Fecha);
        Assert.False(inicial.Entrada.TieneValor);
        Assert.False(inicial.Salida.TieneValor);
        Assert.True(inicial.Saldo.TieneValor);

        // Hay al menos una entrada y una salida entre los movimientos.
        Assert.Contains(r.Filas, f => !f.EsInicial && f.Entrada.TieneValor);
        var salida = Assert.Single(r.Filas, f => f.Salida.TieneValor);
        Assert.True(salida.Salida.Cantidad < 0); // ventas negativas, como InventoryCosts
    }

    [Fact]
    public async Task Filtro_por_cuenta_trae_todos_los_items_de_esa_cuenta()
    {
        var r = await Repo.GenerarAsync(Filtro(cuenta: "13101"));

        var ids = r.Filas.Select(f => f.ItemId).Distinct().ToList();
        Assert.Contains("CS-001", ids);
        Assert.Contains("CS-012", ids);
        Assert.DoesNotContain("CW-001", ids); // esa es 13103
    }

    [Fact]
    public async Task Filtro_por_rango_de_item()
    {
        var r = await Repo.GenerarAsync(Filtro(itemDesde: "CS-001", itemHasta: "CS-012"));

        var ids = r.Filas.Select(f => f.ItemId).Distinct().ToList();
        Assert.Equal(new[] { "CS-001", "CS-012" }, ids.OrderBy(x => x));
    }

    [Fact]
    public async Task El_item_vacio_se_incluye_u_oculta_segun_el_flag()
    {
        // CS-099: sólo un `.INICIAL.` en cero, sin movimientos.
        var conVacios = await Repo.GenerarAsync(Filtro(cuenta: "13101", incluirVacios: true));
        Assert.Contains("CS-099", conVacios.Filas.Select(f => f.ItemId));

        var sinVacios = await Repo.GenerarAsync(Filtro(cuenta: "13101", incluirVacios: false));
        Assert.DoesNotContain("CS-099", sinVacios.Filas.Select(f => f.ItemId));
        // Los que sí tienen movimientos siguen.
        Assert.Contains("CS-012", sinVacios.Filas.Select(f => f.ItemId));
    }

    [Fact]
    public async Task GenerarParaRuc_ignora_el_ruc()
    {
        var f = Filtro(items: new[] { "CS-012" });
        var porRuc = await Repo.GenerarParaRucAsync("1792051800001", f);
        var normal = await Repo.GenerarAsync(f);
        Assert.Equal(normal.Filas.Count, porRuc.Filas.Count);
    }
}
