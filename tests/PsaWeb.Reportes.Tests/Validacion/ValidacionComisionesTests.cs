using ClosedXML.Excel;
using PsaWeb.Modules.Reportes.Comisiones;

namespace PsaWeb.Reportes.Tests.Validacion;

/// <summary>
/// Valida el reporte de Comisiones real (repositorio ODBC + armador) contra el Sage local de Radio FM Efemedio y
/// contra el Excel de Access (<c>COMISIONES EFEMEDIO.xlsx</c>, recibos 5122–5146). Opcional: se salta sin las
/// variables de entorno de <see cref="EntornoSage"/> o sin el archivo.
/// </summary>
public class ValidacionComisionesTests
{
    private static readonly FiltroComisiones Rango5122a5146 = new(ReciboDesde: "5122", ReciboHasta: "5146");

    private static OdbcComisionesRepository Repo() => new(EntornoSage.Acceso(EntornoSage.CadenaEfemedio!));

    [SkippableFact]
    public async Task Repositorio_ODBC_reproduce_las_41_filas_del_excel_de_Access()
    {
        Skip.If(EntornoSage.CadenaEfemedio is null, "Sin PSAWEB_TEST_SAGE_EFEMEDIO (Sage local de Efemedio).");
        Skip.IfNot(File.Exists(EntornoSage.RutaExcelComisiones), "Excel de referencia no está en esta máquina.");

        var resultado = await Repo().GenerarAsync(Rango5122a5146);

        using var wb = new XLWorkbook(EntornoSage.RutaExcelComisiones);
        var ws = wb.Worksheet(1);

        // Excel → filas: cabecera de cliente (C vacío) o factura.
        var clientesExcel = new List<(string Cliente, string Recibo)>();
        var facturasExcel = new List<(string Cliente, IXLRow Fila)>();
        var cliente = "";
        for (var r = 2; r <= ws.LastRowUsed()!.RowNumber(); r++)
        {
            if (ws.Cell(r, "C").IsEmpty())
            {
                cliente = ws.Cell(r, "A").GetString();
                clientesExcel.Add((cliente, ws.Cell(r, "B").GetString()));
            }
            else
            {
                facturasExcel.Add((cliente, ws.Row(r)));
            }
        }

        Assert.Equal(41, facturasExcel.Count);
        Assert.Equal(41, resultado.Facturas);

        // Mismo orden de clientes (por nombre del recibo) y misma cabecera con el n.º de su primer recibo.
        Assert.Equal(clientesExcel.Select(c => c.Cliente.Trim()), resultado.Grupos.Select(g => g.Cliente.Trim()));
        Assert.Equal(clientesExcel.Select(c => c.Recibo), resultado.Grupos.Select(g => g.ReciboEncabezado));

        var porClave = resultado.Grupos
            .SelectMany(g => g.Filas.Select(f => (Cliente: g.Cliente.Trim(), Fila: f)))
            .ToDictionary(x => (x.Fila.Factura, x.Fila.Recibo));

        foreach (var (clienteExcel, fx) in facturasExcel)
        {
            var clave = (fx.Cell("A").GetString(), fx.Cell("J").GetString());
            Assert.True(porClave.TryGetValue(clave, out var x), $"Falta la factura {clave.Item1} / recibo {clave.Item2}.");
            var f = x.Fila;

            Assert.Equal(clienteExcel.Trim(), x.Cliente);
            Assert.Equal(DateOnly.FromDateTime(fx.Cell("B").GetDateTime()), f.Fecha);
            Assert.Equal(DateOnly.FromDateTime(fx.Cell("K").GetDateTime()), f.FechaRecibo);
            Assert.Equal((decimal)fx.Cell("C").GetDouble(), f.Subtotal, 2);
            Assert.Equal((decimal)fx.Cell("D").GetDouble(), f.Iva, 2);
            Assert.Equal((decimal)fx.Cell("E").GetDouble(), f.Total ?? 0m, 2);
            Assert.Equal((decimal)fx.Cell("F").GetDouble(), f.Retencion, 2);
            Assert.Equal((decimal)fx.Cell("G").GetDouble(), f.Cruce, 2);
            Assert.Equal((decimal)fx.Cell("H").GetDouble(), f.Abono, 2);
            Assert.Equal((decimal)fx.Cell("I").GetDouble(), f.Saldo ?? 0m, 2);
        }
    }

    [SkippableFact]
    public async Task Bug_C2_el_rango_de_texto_deja_entrar_los_recibos_513_y_514_y_el_numerico_no()
    {
        Skip.If(EntornoSage.CadenaEfemedio is null, "Sin PSAWEB_TEST_SAGE_EFEMEDIO (Sage local de Efemedio).");

        var fiel = await Repo().GenerarAsync(Rango5122a5146);
        var numerico = await Repo().GenerarAsync(Rango5122a5146 with { RangoNumerico = true });

        var recibosFiel = fiel.Filas.Select(f => f.Recibo).Distinct().ToList();
        Assert.Contains("513", recibosFiel);
        Assert.Contains("514", recibosFiel);

        var recibosNumerico = numerico.Filas.Select(f => f.Recibo).Distinct().ToList();
        Assert.DoesNotContain("513", recibosNumerico);
        Assert.DoesNotContain("514", recibosNumerico);
        Assert.All(recibosNumerico, r => Assert.InRange(long.Parse(r), 5122, 5146));
        Assert.Equal(fiel.Facturas - 2, numerico.Facturas); // las 2 facturas de 2012 (recibos 513 y 514)
    }

    [SkippableFact]
    public async Task Bug_C1_el_abono_heredado_usa_el_total_pagado_de_la_factura_y_la_correccion_el_importe_del_recibo()
    {
        Skip.If(EntornoSage.CadenaEfemedio is null, "Sin PSAWEB_TEST_SAGE_EFEMEDIO (Sage local de Efemedio).");

        var fiel = await Repo().GenerarAsync(Rango5122a5146);
        var corregido = await Repo().GenerarAsync(Rango5122a5146 with { AbonoPorRecibo = true });

        // 001-001-000013912 (recibo 5132): Access muestra 129,80; ese recibo sólo aplicó 1,32.
        Assert.Equal(129.80m, fiel.Filas.Single(f => f.Factura == "001-001-000013912").Abono);
        Assert.Equal(1.32m, corregido.Filas.Single(f => f.Factura == "001-001-000013912").Abono);

        // Donde el recibo cubrió todo lo pendiente, ambos cálculos coinciden (001-001-000015509: 1.306,30).
        Assert.Equal(1306.30m, fiel.Filas.Single(f => f.Factura == "001-001-000015509").Abono);
        Assert.Equal(1306.30m, corregido.Filas.Single(f => f.Factura == "001-001-000015509").Abono);

        // El corregido nunca supera lo aplicado por el recibo.
        Assert.All(corregido.Filas, f => Assert.Equal(Math.Abs(f.ImporteRecibo), f.Abono));
    }

    [SkippableFact]
    public async Task El_filtro_por_fecha_del_recibo_acota_el_resultado_real()
    {
        Skip.If(EntornoSage.CadenaEfemedio is null, "Sin PSAWEB_TEST_SAGE_EFEMEDIO (Sage local de Efemedio).");

        var julio = await Repo().GenerarAsync(new FiltroComisiones(
            FechaReciboDesde: new DateOnly(2026, 7, 1), FechaReciboHasta: new DateOnly(2026, 7, 31)));

        Assert.False(julio.SinDatos);
        Assert.All(julio.Filas, f => Assert.Equal(7, f.FechaRecibo.Month));
        Assert.All(julio.Filas, f => Assert.Equal(2026, f.FechaRecibo.Year));
    }
}
