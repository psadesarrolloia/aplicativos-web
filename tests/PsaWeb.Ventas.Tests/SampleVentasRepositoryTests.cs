using PsaWeb.Modules.Ventas.Data;

namespace PsaWeb.Ventas.Tests;

public class SampleVentasRepositoryTests
{
    private readonly IVentasRepository _repo = new SampleVentasRepository();

    [Fact]
    public async Task Los_ensamblados_solo_aparecen_si_se_piden()
    {
        Assert.DoesNotContain(await _repo.BuscarItemsAsync(new FiltroItems()), i => i.EsEnsamblado);
        Assert.Contains(await _repo.BuscarItemsAsync(new FiltroItems(IncluirEnsamblados: true)), i => i.EsEnsamblado);
    }

    [Fact]
    public async Task Solo_con_existencia_excluye_los_ceros()
    {
        var items = await _repo.BuscarItemsAsync(new FiltroItems(SoloConExistencia: true));
        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.True(i.Existencia > 0));
    }

    [Fact]
    public async Task Cada_palabra_del_texto_debe_coincidir()
    {
        var items = await _repo.BuscarItemsAsync(new FiltroItems("breaker 320"));
        Assert.Single(items);
        Assert.Equal("BR-2049", items[0].Id);
    }

    [Fact]
    public async Task Busca_clientes_por_nombre_o_codigo()
    {
        Assert.Single(await _repo.BuscarClientesAsync("nivel 2"));
        Assert.Empty(await _repo.BuscarClientesAsync("no existe"));
    }
}
