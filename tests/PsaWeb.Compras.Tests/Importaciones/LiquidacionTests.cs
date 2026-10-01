using Microsoft.EntityFrameworkCore;
using PsaWeb.Compras.Importaciones;

namespace PsaWeb.Compras.Tests.Importaciones;

public class LiquidacionTests
{
    private static ItemLiquidacion I(string id, double cantidad, double valor) => new() { ItemId = id, Descripcion = id, Cantidad = cantidad, Valor = valor };

    [Fact]
    public void Prorratea_por_valor_y_el_ultimo_absorbe_el_residuo()
    {
        var items = new List<ItemLiquidacion> { I("B", 2, 300), I("A", 1, 100), I("C", 3, 600) };
        Assert.Null(Liquidaciones.Prorratear(items, 1000, 100.01));
        // Orden por valor: A (10 %), B (30 %), C (último: residuo).
        Assert.Equal(10.0, items[1].Porcentaje, 6);
        Assert.Equal(10.0, items[1].Prorrateo);
        Assert.Equal(30.0, items[0].Prorrateo);
        Assert.Equal(60.01, items[2].Prorrateo, 2);
        Assert.Equal(60.0, items[2].Porcentaje, 6);
        Assert.Equal(100.01, items.Sum(x => x.Prorrateo), 2);
        Assert.Equal(Math.Round(600 + 60.01, 2), items[2].CostoTotal);
        Assert.Equal(items[2].CostoTotal / 3, items[2].CostoUnitario);
    }

    [Fact]
    public void Prorratear_exige_que_los_items_sumen_la_factura()
    {
        var items = new List<ItemLiquidacion> { I("A", 1, 100) };
        Assert.NotNull(Liquidaciones.Prorratear(items, 100.02, 10));
        Assert.Null(Liquidaciones.Prorratear(items, 100.004, 10));
    }

    [Fact]
    public void Totales_separan_gastos_de_la_factura_y_el_costeo_completo_cuadra_con_todas_las_filas()
    {
        var gastos = new List<GastoImportacion>
        {
            new() { Valor = 1000, EsGasto = false },
            new() { Valor = 50 },
            new() { Valor = 25.5 },
        };
        Assert.Equal((75.5, 1000.0), Liquidaciones.Totales(gastos));
        var items = new List<ItemLiquidacion> { I("A", 1, 400), I("B", 1, 600) };
        Assert.False(Liquidaciones.CosteoCompleto(items, gastos));
        Liquidaciones.Prorratear(items, 1000, 75.5);
        Assert.True(Liquidaciones.CosteoCompleto(items, gastos));
    }

    [Fact]
    public void Conciliar_conserva_la_marca_de_factura_por_fila_y_senala_nuevas_y_desaparecidas()
    {
        var f = new DateTime(2026, 8, 1);
        var sage = new List<GastoImportacion>
        {
            new() { Fecha = f, Referencia = "FAC-1", Descripcion = "FACTURA", Valor = 1000 },
            new() { Fecha = f, Referencia = "001-001-1", Descripcion = "FLETE", Valor = 50 },
            new() { Fecha = f, Referencia = "001-001-2", Descripcion = "SEGURO", Valor = 20 },
        };
        var guardados = new List<GastoImportacion>
        {
            new() { Fecha = f, Referencia = "FAC-1", Descripcion = "FACTURA", Valor = 1000, EsGasto = false },
            new() { Fecha = f, Referencia = "001-001-1", Descripcion = "FLETE", Valor = 50 },
            new() { Fecha = f, Referencia = "X", Descripcion = "BORRADO", Valor = 5 },
        };
        var (g, desaparecidos) = Liquidaciones.Conciliar(sage, guardados);
        Assert.False(g[0].EsGasto);
        Assert.True(g[1].EsGasto);
        Assert.False(g[1].Nueva);
        Assert.True(g[2].Nueva);
        Assert.Single(desaparecidos);
        Assert.True(sage[0].EsGasto); // no toca la lista de Sage
    }

    [Theory]
    [InlineData("IMPORTACION 41-2026", "041-2026")]
    [InlineData("IMPORTACION 005-2010", "005-2010")]
    [InlineData("IMPORTACION 7 - 2020", "007-2020")]
    [InlineData("IMPORTACIONES EN TRANSITO", null)]
    public void Numero_propuesto_desde_la_cuenta(string descripcion, string? esperado) =>
        Assert.Equal(esperado, Liquidaciones.NumeroPropuesto(descripcion));

    [Fact]
    public void Referencia_y_separacion_como_el_exe()
    {
        Assert.Equal("LIQ IMPORT-041-2026", Liquidaciones.Referencia("LIQ IMPORT", "041-2026"));
        Assert.Equal("041-2026", Liquidaciones.Referencia("", "041-2026"));
        Assert.Equal(("LIQ IMPORT", "041-2026"), Liquidaciones.Separar("LIQ IMPORT-041-2026"));
        Assert.Equal(("", "041"), Liquidaciones.Separar("041"));
        Assert.Equal("LIQ IMPORT 041-2026", PsaWeb.SageBridge.Contratos.ReferenciasLiquidacion.DeCompra("LIQ IMPORT-041-2026"));
    }

    [Theory]
    [InlineData(0, 100, false, false, EstadoImportacion.SinDatos)]
    [InlineData(3, 0.04, true, true, EstadoImportacion.Liquidada)]
    [InlineData(3, 500, false, false, EstadoImportacion.EnTransito)]
    [InlineData(3, 500, true, false, EstadoImportacion.Guardada)]
    [InlineData(3, 500, true, true, EstadoImportacion.EnProceso)]
    public void Estado_como_sageImportMgm(int filas, double saldo, bool guardada, bool oc, EstadoImportacion esperado) =>
        Assert.Equal(esperado, EstadosImportacion.Calcular(filas, saldo, guardada, oc));

    [Fact]
    public void Armador_valida_y_arma_una_linea_por_item_con_valor_mas_prorrateo()
    {
        var items = new List<ItemLiquidacion> { I("A", 2, 400), I("B", 1, 600) };
        var gastos = new List<GastoImportacion> { new() { Valor = 1000, EsGasto = false }, new() { Valor = 100 } };
        var proveedores = new HashSet<string> { "PROV" };
        var sinProrratear = ArmadorOcLiquidacion.Armar(new("13803", "PROV", new DateTime(2026, 8, 25), "LIQ IMPORT-041-2026", null, items, gastos), proveedores);
        Assert.Null(sinProrratear.Payload);
        Liquidaciones.Prorratear(items, 1000, 100);
        var sinProveedor = ArmadorOcLiquidacion.Armar(new("13803", "OTRO", new DateTime(2026, 8, 25), "LIQ IMPORT-041-2026", null, items, gastos), proveedores);
        Assert.Contains(sinProveedor.Errores, e => e.Contains("proveedor"));
        var (p, errores) = ArmadorOcLiquidacion.Armar(new("13803", "PROV", new DateTime(2026, 8, 25), "LIQ IMPORT-041-2026", 99, items, gastos), proveedores);
        Assert.Empty(errores);
        Assert.Equal("2026-08-25", p!.Fecha);
        Assert.Equal(99, p.PostOrder);
        Assert.Equal(new[] { 440m, 660m }, p.Lineas.Select(x => x.Monto));
        Assert.Equal(2m, p.Lineas[0].Cantidad);
    }

    /// <summary>Prorrateo recalculado desde lo guardado en PeachEBills, en todas las empresas (incluida SANCEV), sin Sage.</summary>
    [SkippableFact]
    public async Task Prorrateo_de_todas_las_liquidaciones_guardadas_de_todas_las_empresas()
    {
        await using var db = PsaWeb.Compras.Tests.Validacion.EntornoCompras.PeachEbills();
        List<(string? Ruc, string Cuenta)> cuentas;
        try
        {
            cuentas = (await db.ImportCost.AsNoTracking().Select(x => new { x.TransmitterRuc, x.AccountId }).Distinct().ToListAsync())
                .Select(x => (x.TransmitterRuc, x.AccountId)).ToList();
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Skip.If(true, "Sin PeachEBills local.");
            return;
        }
        var difs = new List<string>();
        var n = 0;
        foreach (var (ruc, cuenta) in cuentas)
        {
            var g = await RepositorioLiquidaciones.UltimaAsync(db, ruc!, cuenta);
            if (g is null || g.Items.Count == 0 || g.Items.All(x => x.Prorrateo == 0)) continue;
            n++;
            var items = g.Items.Select(x => x.Copia()).ToList();
            var (gastos, factura) = Liquidaciones.Totales(g.Gastos);
            var error = Liquidaciones.Prorratear(items, factura, gastos);
            if (error is not null || items.Zip(g.Items).Any(p => p.First.Prorrateo != p.Second.Prorrateo || Math.Abs(p.First.Porcentaje - p.Second.Porcentaje) > 1e-12))
            {
                difs.Add($"{ruc} {cuenta}: {error}");
            }
        }
        Assert.True(n > 200, $"Solo {n} liquidaciones prorrateadas.");
        Assert.True(difs.Count == 0, string.Join("\n", difs));
    }
}
