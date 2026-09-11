using PsaWeb.Modules.Kardex.Data;

namespace PsaWeb.Kardex.Tests;

public class SeleccionItemsTests
{
    private static readonly IReadOnlyList<ItemStock> Items = new[]
    {
        new ItemStock("CS-001", "Casing 20", "CASING", "13101"),
        new ItemStock("CS-012", "Casing 9 5/8", "CASING", "13101"),
        new ItemStock("CS-020", "Casing 7", "CASING", "13101"),
        new ItemStock("CW-001", "Wellhead", "WELLHEAD", "13103"),
    };

    private static FiltroKardex F(
        IReadOnlyList<string>? ids = null, string? cuenta = null, IReadOnlyList<string>? cuentas = null,
        string? desde = null, string? hasta = null)
        => new(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30),
               ids ?? Array.Empty<string>(),
               cuentas ?? (cuenta is null ? Array.Empty<string>() : new[] { cuenta }),
               desde, hasta);

    [Fact]
    public void Ids_explicitos_mandan_sobre_todo()
    {
        var r = SeleccionItems.Filtrar(Items, F(ids: new[] { "cs-012", "CW-001" }, cuenta: "13101"));
        Assert.Equal(new[] { "CS-012", "CW-001" }, r.Select(i => i.Id));
    }

    [Fact]
    public void Por_cuenta_trae_todos_los_de_esa_cuenta()
    {
        var r = SeleccionItems.Filtrar(Items, F(cuenta: "13103"));
        Assert.Equal(new[] { "CW-001" }, r.Select(i => i.Id));
    }

    [Fact]
    public void Por_varias_cuentas_trae_los_items_de_todas()
    {
        var r = SeleccionItems.Filtrar(Items, F(cuentas: new[] { "13101", "13103" }));
        Assert.Equal(new[] { "CS-001", "CS-012", "CS-020", "CW-001" }, r.Select(i => i.Id));
    }

    [Fact]
    public void Por_rango_inclusive_en_ambos_extremos()
    {
        var r = SeleccionItems.Filtrar(Items, F(desde: "CS-001", hasta: "CS-012"));
        Assert.Equal(new[] { "CS-001", "CS-012" }, r.Select(i => i.Id));
    }

    [Fact]
    public void Rango_con_un_solo_extremo()
    {
        Assert.Equal(new[] { "CS-020", "CW-001" },
            SeleccionItems.Filtrar(Items, F(desde: "CS-020")).Select(i => i.Id));
        Assert.Equal(new[] { "CS-001", "CS-012" },
            SeleccionItems.Filtrar(Items, F(hasta: "CS-012")).Select(i => i.Id));
    }

    [Fact]
    public void Sin_nada_no_devuelve_nada()
    {
        Assert.Empty(SeleccionItems.Filtrar(Items, F()));
    }
}
