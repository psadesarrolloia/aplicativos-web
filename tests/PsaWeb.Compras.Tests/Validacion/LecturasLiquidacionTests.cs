using System.Data.Odbc;
using System.Text;
using PsaWeb.Compras.Importaciones;
using PsaWeb.Modules.Compras.Importaciones;
using Xunit.Abstractions;

namespace PsaWeb.Compras.Tests.Validacion;

/// <summary>
/// F3 de Liquidación de Importaciones, solo lectura contra la copia: las consultas que usan las páginas (lista de cuentas con
/// estado, filas de una cuenta, OC y compra, ítems, proveedores), la conciliación C2 sobre datos reales (cuántas liquidaciones
/// perdían la marca de la factura con la regla del `.exe`) y el reporte de dos liquidaciones reales (se deja en %TEMP%).
/// </summary>
public class LecturasLiquidacionTests(ITestOutputHelper salida)
{
    [SkippableFact]
    public async Task Lecturas_de_las_paginas_y_reporte_con_datos_reales()
    {
        Skip.If(EntornoCompras.CadenaSage is null, "Sin PSAWEB_TEST_SAGE_COMPRAS.");
        Skip.If(Environment.Is64BitProcess, "El driver ODBC de Pervasive es de 32 bits.");
        await using var cn = new OdbcConnection(EntornoCompras.CadenaSage);
        await cn.OpenAsync();
        await using var db = EntornoCompras.PeachEbills();
        var informe = new StringBuilder();

        var cuentas = await LectorImportaciones.CuentasAsync(cn);
        var guardadas = await RepositorioLiquidaciones.ResumenAsync(db, EntornoCompras.Ruc);
        var ocs = await LectorImportaciones.OcsExistentesAsync(cn, guardadas.Values.Where(x => x.PostOrder is not null).Select(x => x.PostOrder!.Value).ToList());
        var estados = cuentas.Select(c => EstadosImportacion.Calcular(c.Movimientos, c.Saldo, guardadas.ContainsKey(c.Cuenta),
            guardadas.TryGetValue(c.Cuenta, out var g) && g.PostOrder is { } po && ocs.Contains(po))).ToList();
        informe.AppendLine($"Cuentas IMPORTACION: {cuentas.Count} (inactivas {cuentas.Count(x => x.Inactiva)}); " +
                           string.Join(", ", estados.GroupBy(x => x).Select(x => $"{EstadosImportacion.Texto(x.Key)} {x.Count()}")));
        Assert.True(cuentas.Count > 400);
        Assert.Contains(estados, x => x == EstadoImportacion.Liquidada);

        // C2 sobre datos reales: el `.exe` usaba lo guardado solo si la suma de Sage era idéntica.
        int perdian = 0, conMarca = 0, conciliadas = 0;
        foreach (var cuenta in guardadas.Keys)
        {
            var gg = await RepositorioLiquidaciones.UltimaAsync(db, EntornoCompras.Ruc, cuenta);
            if (gg is null || gg.Gastos.All(x => x.EsGasto)) continue;
            conMarca++;
            var sage = await LectorImportaciones.GastosAsync(cn, cuenta);
            if (sage.Sum(x => x.Valor) != gg.Gastos.Sum(x => x.Valor)) perdian++;
            var (c2, _) = Liquidaciones.Conciliar(sage, gg.Gastos);
            if (c2.Any(x => !x.EsGasto)) conciliadas++;
        }
        informe.AppendLine($"C2: {conMarca} liquidaciones con factura marcada; con la regla del .exe perdían la marca {perdian}; con C2 la conservan {conciliadas}.");

        // OC, compra, ítems y proveedores; y el reporte de dos liquidaciones reales.
        foreach (var cuenta in new[] { "13797", "13798" }) // 035 y 036-2026: sin datos de prueba en la copia
        {
            var gg = (await RepositorioLiquidaciones.UltimaAsync(db, EntornoCompras.Ruc, cuenta))!;
            var oc = await LectorImportaciones.OcAsync(cn, gg.PostOrder!.Value);
            Assert.NotNull(oc);
            Assert.NotNull(oc!.PostOrderCompra);
            var porRef = await LectorImportaciones.OcPorReferenciaAsync(cn, oc.Referencia, oc.ProveedorId);
            Assert.Equal(oc.PostOrder, porRef!.PostOrder);
            var c = (await LectorImportaciones.CuentaAsync(cn, cuenta))!;
            var sage = await LectorImportaciones.GastosAsync(cn, cuenta);
            var (gastos, _) = Liquidaciones.Conciliar(sage, gg.Gastos);
            var sinLiq = gastos.Where(x => !(x.Referencia == oc.ReferenciaCompra && x.Descripcion == "LIQUIDACION")).ToList();
            Assert.True(Liquidaciones.CosteoCompleto(gg.Items, sinLiq), $"{cuenta}: costeo incompleto sin la fila de la compra");
            informe.AppendLine($"{cuenta} {c.Descripcion}: OC {oc.Referencia} → compra {oc.ReferenciaCompra}; {sage.Count} filas; costeo completo: {Liquidaciones.CosteoCompleto(gg.Items, sinLiq)}");
            var bytes = ReporteLiquidacion.Generar("CPTDC ECUADOR S. A.", c, sinLiq, gg.Items);
            File.WriteAllBytes(Path.Combine(Path.GetTempPath(), $"psa-f4-{cuenta}.xlsx"), bytes);
        }
        var ap = await LectorImportaciones.CuentaPorPagarAsync(cn);
        var conEspacio = await LectorImportaciones.CompraConEspacioAsync(cn);
        informe.AppendLine($"Cuenta por pagar: {ap}; compra con espacio: {conEspacio}.");
        Assert.Equal("20000", ap);
        Assert.True(conEspacio);
        var items = await LectorImportaciones.ItemsStockAsync(cn);
        var proveedores = await LectorImportaciones.ProveedoresAsync(cn);
        informe.AppendLine($"Ítems de stock: {items.Count}; proveedores: {proveedores.Count}.");
        Assert.Contains(items, x => x.Id == "CA-008");
        Assert.Contains(proveedores, x => x.Id == "CPTDC-IMPORT-CHINA");

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "psa-f3-lecturas.txt"), informe.ToString());
        salida.WriteLine(informe.ToString());
    }
}
