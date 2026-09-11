using PsaWeb.Modules.Kardex.Data;

namespace PsaWeb.Kardex.Tests;

public class FiltroKardexTests
{
    private static readonly DateOnly D1 = new(2026, 9, 1);
    private static readonly DateOnly D10 = new(2026, 9, 10);

    private static FiltroKardex Filtro(
        DateOnly? desde = null, DateOnly? hasta = null,
        IReadOnlyList<string>? items = null,
        string? cuenta = null, string? itemDesde = null, string? itemHasta = null)
        => new(desde ?? D1, hasta ?? D10, items ?? Array.Empty<string>(),
               string.IsNullOrWhiteSpace(cuenta) ? Array.Empty<string>() : new[] { cuenta },
               itemDesde, itemHasta);

    [Fact]
    public void Sin_ningun_acotador_no_tiene_acotador()
    {
        Assert.False(Filtro().TieneAcotador);
    }

    [Theory]
    [InlineData("13101", null, null)]
    [InlineData(null, "CS-001", null)]
    [InlineData(null, null, "CS-999")]
    public void Cuenta_o_rango_cuentan_como_acotador(string? cuenta, string? desde, string? hasta)
    {
        Assert.True(Filtro(cuenta: cuenta, itemDesde: desde, itemHasta: hasta).TieneAcotador);
    }

    [Fact]
    public void Lista_de_items_cuenta_como_acotador()
    {
        Assert.True(Filtro(items: new[] { "CS-012" }).TieneAcotador);
    }

    [Fact]
    public void Cuenta_en_blanco_no_cuenta()
    {
        Assert.False(Filtro(cuenta: "   ").TieneAcotador);
    }

    [Fact]
    public void Rango_valido_exige_hasta_posterior_a_desde()
    {
        Assert.True(Filtro(desde: D1, hasta: D10).RangoValido);
        Assert.False(Filtro(desde: D1, hasta: D1).RangoValido);      // mismo día: como el .exe, no vale
        Assert.False(Filtro(desde: D10, hasta: D1).RangoValido);
    }

    [Fact]
    public void IncluirVacios_por_defecto_es_true()
    {
        Assert.True(Filtro().IncluirVacios);
    }
}
