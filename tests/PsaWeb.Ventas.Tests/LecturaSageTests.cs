using System.Data.Odbc;
using System.Globalization;
using PsaWeb.Modules.Ventas.Data;
using PsaWeb.Sage50;

namespace PsaWeb.Ventas.Tests;

/// <summary>
/// Validación contra una base real de Sage (solo SELECT). Se activa con la variable de entorno PSAWEB_TEST_SAGE_VENTAS = cadena ODBC
/// (probado con SANCEV, DBQ=sancevcialtda2202520) y corre en x86 por el driver de Pervasive.
/// </summary>
public class LecturaSageTests
{
    private sealed class Fabrica(string cadena) : ISageConnectionFactory
    {
        public OdbcConnection CreateConnection() => new(cadena);
        public OdbcConnection CreateConnection(string connectionString) => new(connectionString);
    }

    private static string Cadena()
    {
        var c = Environment.GetEnvironmentVariable("PSAWEB_TEST_SAGE_VENTAS");
        Skip.If(string.IsNullOrWhiteSpace(c), "Sin PSAWEB_TEST_SAGE_VENTAS.");
        Skip.If(Environment.Is64BitProcess, "El driver ODBC de Pervasive es de 32 bits.");
        return c!;
    }

    private static OdbcVentasRepository Repo(string cadena) => new(new Fabrica(cadena), new SinShellResolverEmpresaSage());

    [SkippableFact]
    public async Task La_existencia_es_igual_al_ultimo_saldo_de_InventoryCosts()
    {
        var cadena = Cadena();
        var items = await Repo(cadena).BuscarItemsAsync(new FiltroItems(SoloConExistencia: true, Maximo: 60));
        Assert.NotEmpty(items);

        await using var cn = new OdbcConnection(cadena);
        await cn.OpenAsync();
        foreach (var item in items)
        {
            await using var cmd = new OdbcCommand(
                "SELECT TOP 1 i.Quantity FROM InventoryCosts i, LineItem l WHERE i.ItemRecNumber = l.ItemRecordNumber AND l.ItemID = ? AND i.MajorType = 3 " +
                "ORDER BY i.TransDate DESC, i.PostOrderNumber DESC, i.RowNumber DESC", cn);
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = item.Id });
            var saldo = Convert.ToDecimal(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            Assert.True(saldo == item.Existencia, $"{item.Id}: existencia {item.Existencia} distinta del último saldo {saldo}");
        }
    }

    [SkippableFact]
    public async Task Los_clientes_traen_nivel_cupo_y_vendedor()
    {
        var clientes = await Repo(Cadena()).BuscarClientesAsync("CARLOTA RODRIGUEZ");
        var c = Assert.Single(clientes);
        Assert.Equal(1, c.NivelPrecio);
        Assert.Equal(2, c.ListaDePrecios);
        Assert.Equal(30, c.DiasCredito);
        Assert.False(string.IsNullOrEmpty(c.Vendedor));
    }

    [SkippableFact]
    public async Task El_precio_del_cliente_de_nivel_1_es_el_de_la_lista_2()
    {
        // Datos del 2026-10-02 en SANCEV: RT18Z-32/2P EBAS = 6,95 (lista 1) y 7,31 (lista 2).
        var repo = Repo(Cadena());
        var item = Assert.Single(await repo.BuscarItemsAsync(new FiltroItems("RT18Z-32/2P EBAS")));
        Assert.Equal(6.95m, item.PrecioParaNivel(0));
        Assert.Equal(7.31m, item.PrecioParaNivel(1));
    }

    [SkippableFact]
    public async Task Las_categorias_y_los_ensamblados_se_separan()
    {
        var repo = Repo(Cadena());
        var sinEnsamblados = await repo.CategoriasAsync(false);
        var conEnsamblados = await repo.CategoriasAsync(true);
        Assert.DoesNotContain("TE", sinEnsamblados);
        Assert.Contains("TE", conEnsamblados);
    }
}
