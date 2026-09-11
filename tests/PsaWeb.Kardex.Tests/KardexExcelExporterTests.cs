using ClosedXML.Excel;
using PsaWeb.Modules.Kardex.Data;
using PsaWeb.Modules.Kardex.Export;

namespace PsaWeb.Kardex.Tests;

public class KardexExcelExporterTests
{
    private static readonly DateOnly Desde = new(2026, 8, 1);
    private static readonly DateOnly Hasta = new(2026, 8, 31);

    private static readonly KardexExcelExporter Exporter = new();

    private static MovimientoKardex Mov(decimal cant, decimal cu, decimal ct) => new(cant, cu, ct);

    private static FilaKardex Inicial(string item) => new(
        "13101", item, $"Nombre {item}", "CASING", Desde, ".INICIAL.",
        MovimientoKardex.Vacio, MovimientoKardex.Vacio, Mov(1000m, 30m, 30_000m), EsInicial: true);

    private static FilaKardex Compra(string item, DateOnly fecha) => new(
        "13101", item, $"Nombre {item}", "CASING", fecha, "LIQ IMPORT 007",
        Mov(500m, 30m, 15_000m), MovimientoKardex.Vacio, MovimientoKardex.Vacio, EsInicial: false);

    private static FilaKardex Venta(string item, DateOnly fecha) => new(
        "13101", item, $"Nombre {item}", "CASING", fecha, "001-001-9",
        MovimientoKardex.Vacio, Mov(-320m, 30m, -9_600m), MovimientoKardex.Vacio, EsInicial: false);

    private static ResultadoKardex UnItem() => new(new[]
    {
        Inicial("CS-012"),
        Compra("CS-012", new DateOnly(2026, 8, 5)),
        Venta("CS-012", new DateOnly(2026, 8, 20)),
    });

    private static IXLWorksheet Abrir(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return new XLWorkbook(ms).Worksheet(1);
    }

    [Fact]
    public void Genera_un_xlsx_valido_con_titulo_y_periodo()
    {
        var bytes = Exporter.Generar(UnItem(), Desde, Hasta);

        Assert.NotEmpty(bytes);
        var ws = Abrir(bytes);
        Assert.Equal("KARDEX DE INVENTARIOS", ws.Cell(1, 1).GetString());
        Assert.Contains("01/08/2026", ws.Cell(3, 1).GetString());
        Assert.Contains("31/08/2026", ws.Cell(3, 1).GetString());
        Assert.False(ws.ShowGridLines);
    }

    [Fact]
    public void Encabezado_por_item_con_grupos_y_subcolumnas()
    {
        var ws = Abrir(Exporter.Generar(UnItem(), Desde, Hasta));

        // Fila 5 = grupos; fila 6 = etiquetas + subcolumnas.
        Assert.Equal("Entradas", ws.Cell(5, 6).GetString());
        Assert.Equal("Salidas", ws.Cell(5, 9).GetString());
        Assert.Equal("Saldos", ws.Cell(5, 12).GetString());
        Assert.Equal("Cuenta", ws.Cell(6, 1).GetString());
        Assert.Equal("Fecha", ws.Cell(6, 4).GetString());
        Assert.Equal("Cant.", ws.Cell(6, 6).GetString());
        Assert.Equal("Costo U.", ws.Cell(6, 7).GetString());
        Assert.Equal("Costo T.", ws.Cell(6, 14).GetString());
        Assert.Equal(KardexExcelExporter.Navy, ws.Cell(5, 1).Style.Fill.BackgroundColor);
    }

    [Fact]
    public void Fila_inicial_lleva_los_saldos_como_valores_literales()
    {
        var ws = Abrir(Exporter.Generar(UnItem(), Desde, Hasta));

        // Datos desde la fila 7.
        Assert.Equal("CS-012", ws.Cell(7, 2).GetString());
        Assert.Equal("Nombre CS-012", ws.Cell(7, 3).GetString());
        Assert.Equal(".INICIAL.", ws.Cell(7, 5).GetString());
        Assert.False(ws.Cell(7, 12).HasFormula);
        Assert.Equal(1000m, ws.Cell(7, 12).GetValue<decimal>());
        Assert.Equal(30_000m, ws.Cell(7, 14).GetValue<decimal>());
    }

    [Fact]
    public void Filas_de_movimiento_llevan_formula_de_saldo_corrido()
    {
        var ws = Abrir(Exporter.Generar(UnItem(), Desde, Hasta));

        // Fila 8 = compra: entrada en F/G/H, saldo = F8+I8+L7.
        Assert.Equal(500m, ws.Cell(8, 6).GetValue<decimal>());
        Assert.True(ws.Cell(8, 12).HasFormula);
        Assert.Contains("L7", ws.Cell(8, 12).FormulaA1.Replace(" ", ""));
        Assert.Contains("N7", ws.Cell(8, 14).FormulaA1.Replace(" ", ""));

        // Fila 9 = venta: cantidad negativa en I/J/K.
        Assert.Equal(-320m, ws.Cell(9, 9).GetValue<decimal>());
        Assert.Contains("L8", ws.Cell(9, 12).FormulaA1.Replace(" ", ""));
    }

    [Fact]
    public void Aplica_formato_numerico_y_de_fecha()
    {
        var ws = Abrir(Exporter.Generar(UnItem(), Desde, Hasta));

        Assert.Equal("#,##0.00", ws.Cell(8, 6).Style.NumberFormat.Format);
        Assert.Equal("dd/MM/yyyy", ws.Cell(8, 4).Style.DateFormat.Format);
    }

    [Fact]
    public void Repite_el_encabezado_por_cada_item()
    {
        var dos = new ResultadoKardex(new[]
        {
            Inicial("CS-001"), Compra("CS-001", new DateOnly(2026, 8, 5)), Venta("CS-001", new DateOnly(2026, 8, 20)),
            Inicial("CS-012"), Compra("CS-012", new DateOnly(2026, 8, 6)),
        });

        var ws = Abrir(Exporter.Generar(dos, Desde, Hasta));

        // item1: encabezado 5-6, datos 7-9. +2 de separación => item2 encabezado en 12.
        Assert.Equal("Entradas", ws.Cell(12, 6).GetString());
        Assert.Equal("CS-012", ws.Cell(14, 2).GetString());
    }

    [Fact]
    public void Sin_filas_no_revienta()
    {
        var ws = Abrir(Exporter.Generar(ResultadoKardex.Vacio, Desde, Hasta));
        Assert.Equal("KARDEX DE INVENTARIOS", ws.Cell(1, 1).GetString());
        Assert.Contains("Sin movimientos", ws.Cell(5, 1).GetString());
    }

    [Fact]
    public void NombreArchivo_usa_el_rango()
    {
        Assert.Equal("Kardex_20260801_20260831.xlsx", Exporter.NombreArchivo(Desde, Hasta));
    }
}
