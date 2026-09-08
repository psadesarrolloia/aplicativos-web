using System.Data.Odbc;
using PsaWeb.Comprobantes.Venta.InfoAdicional;

namespace PsaWeb.Comprobantes.Tests;

public class LectorInfoAdicionalTests
{
    // Cuando todas las filas son de "valor fijo" no se toca la conexión ODBC,
    // así que se puede probar la parte de ensamblado/orden sin base.
    private static Task<IReadOnlyList<PsaWeb.Datil.Model.InfoAdicionalItem>> Armar(
        params ConfigInfoAdicional[] config)
        => LectorInfoAdicional.ArmarAsync(null!, "PO1", config);

    [Fact]
    public async Task Valores_fijos_se_devuelven_en_orden()
    {
        var lista = await Armar(
            new ConfigInfoAdicional("Vendedor", OrderNum: 2, null, null, "Ana"),
            new ConfigInfoAdicional("Sucursal", OrderNum: 1, null, null, "Norte"));

        Assert.Equal(2, lista.Count);
        Assert.Equal("Sucursal", lista[0].Nombre);
        Assert.Equal("Norte", lista[0].Valor);
        Assert.Equal("Vendedor", lista[1].Nombre);
    }

    [Fact]
    public async Task Valor_fijo_vacio_o_nulo_se_omite()
    {
        var lista = await Armar(
            new ConfigInfoAdicional("A", 1, null, null, null),
            new ConfigInfoAdicional("B", 2, null, null, ""),
            new ConfigInfoAdicional("C", 3, null, null, "ok"));

        Assert.Equal("C", Assert.Single(lista).Nombre);
    }

    [Fact]
    public async Task Config_con_SourceTable_pero_SourceValue_invalido_se_ignora()
    {
        // SourceValue con ; o espacios no pasa el validador de identificador ->
        // no se ejecuta ninguna consulta (conexión null no revienta).
        var lista = await Armar(
            new ConfigInfoAdicional("Malo", 1, "JrnlHdr", "Reference; DROP TABLE x", null),
            new ConfigInfoAdicional("Vacio", 2, "JrnlRow", "  ", null),
            new ConfigInfoAdicional("Fijo", 3, null, null, "sí"));

        Assert.Equal("Fijo", Assert.Single(lista).Nombre);
    }

    [Fact]
    public async Task SourceTable_desconocida_se_ignora()
    {
        var lista = await Armar(
            new ConfigInfoAdicional("X", 1, "TablaRara", "Campo", null),
            new ConfigInfoAdicional("Fijo", 2, null, null, "v"));

        Assert.Equal("Fijo", Assert.Single(lista).Nombre);
    }
}
