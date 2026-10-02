using PsaWeb.Modules.Ventas.Data;

namespace PsaWeb.Ventas.Tests;

public class PreciosDeVentaTests
{
    private static ItemVenta Item(params decimal[] precios)
    {
        var p = new decimal[PreciosDeVenta.NivelesMaximos];
        Array.Copy(precios, p, precios.Length);
        return new ItemVenta("X", "Ítem X", "CAT", false, "UND", 10m, p);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(9, 9)]
    [InlineData(-1, -1)]
    [InlineData(10, -1)]
    public void El_nivel_del_cliente_es_0_based_y_se_ubica_en_el_arreglo_de_listas(int nivel, int posicion) =>
        Assert.Equal(posicion, PreciosDeVenta.PosicionDeLista(nivel));

    [Fact]
    public void Cliente_de_nivel_1_toma_la_lista_2_de_Sage()
    {
        // Caso real SANCEV (2026-10-02): ítem RT18Z-32/2P EBAS, listas 6,95 / 7,31; el cliente CARLOTA RODRIGUEZ tiene PriceLevel 1.
        var item = Item(6.95m, 7.31m);
        Assert.Equal(6.95m, item.PrecioParaNivel(0));
        Assert.Equal(7.31m, item.PrecioParaNivel(1));
    }

    [Fact]
    public void Una_lista_sin_precio_o_un_nivel_invalido_no_inventa_precio()
    {
        var item = Item(6.95m, 7.31m);
        Assert.Null(item.PrecioParaNivel(2));
        Assert.Null(item.PrecioParaNivel(-1));
        Assert.Null(item.PrecioParaNivel(99));
        Assert.Equal(2, item.ListasConPrecio);
    }

    [Fact]
    public void La_existencia_suma_compras_y_ventas_pero_no_los_saldos()
    {
        var movimientos = new (int, decimal)[] { (1, 30m), (2, -2m), (2, -3m), (3, 25m), (3, 99m) };
        Assert.Equal(25m, PreciosDeVenta.Existencia(movimientos));
    }

    [Fact]
    public void Cliente_calcula_lista_cupo_y_exceso()
    {
        var c = new ClienteVenta("C", "Cliente", "", "", "", 1, 30, 1000m, 400m, "VEND");
        Assert.Equal(2, c.ListaDePrecios);
        Assert.Equal(600m, c.CupoDisponible);
        Assert.False(c.SuperaCupo(600m));
        Assert.True(c.SuperaCupo(600.01m));
    }

    [Fact]
    public void Limite_de_credito_cero_significa_sin_limite_configurado()
    {
        var c = new ClienteVenta("C", "Cliente", "", "", "", 0, 1, 0m, 5000m, null);
        Assert.False(c.SuperaCupo(1_000_000m));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("   ", 0)]
    [InlineData("breaker 3 polos", 3)]
    [InlineData("a b c d e f g", 5)]
    public void Los_terminos_de_busqueda_se_limitan_y_normalizan(string? texto, int cuantos) =>
        Assert.Equal(cuantos, OdbcVentasRepository.Terminos(texto).Count);

    [Fact]
    public void Los_comodines_del_usuario_no_se_pasan_al_LIKE()
    {
        var t = OdbcVentasRepository.Terminos("100% ab_c");
        Assert.Equal(new[] { "100", "ABC" }, t);
    }
}
