using ClosedXML.Excel;
using PsaWeb.Compras.Importaciones;
using PsaWeb.Modules.Compras.Importaciones;

namespace PsaWeb.Compras.Tests.Importaciones;

public class ExcelLiquidacionTests
{
    [Theory]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1234,56", 1234.56)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("$ 1,234.56", 1234.56)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("-15", -15)]
    public void B1_texto_con_separadores_se_convierte_sin_depender_de_la_cultura(string texto, double esperado)
    {
        Assert.Equal(esperado, ExcelDetalleLiquidacion.Numero(new CeldaExcel(null, texto), out var error), 6);
        Assert.Null(error);
    }

    [Fact]
    public void Celda_numerica_se_toma_tal_cual_y_texto_invalido_da_cero_con_error()
    {
        Assert.Equal(804644.65, ExcelDetalleLiquidacion.Numero(new CeldaExcel(804644.65, "804.644,65"), out var e1));
        Assert.Null(e1);
        Assert.Equal(0, ExcelDetalleLiquidacion.Numero(new CeldaExcel(null, "abc"), out var e2));
        Assert.NotNull(e2);
    }

    [Fact]
    public void Lee_la_primera_hoja_con_encabezados_repetidos_y_salta_filas_vacias_y_convierte_con_el_mapeo()
    {
        using var ms = new MemoryStream();
        using (var libro = new XLWorkbook())
        {
            var ws = libro.Worksheets.Add("Detalle");
            ws.Cell(1, 1).Value = "Item Id";
            ws.Cell(1, 2).Value = "Valor";
            ws.Cell(1, 3).Value = "Valor";
            ws.Cell(1, 4).Value = "Cantidad";
            ws.Cell(2, 1).Value = "CA-008";
            ws.Cell(2, 2).Value = 1;
            ws.Cell(2, 3).Value = 804644.65;
            ws.Cell(2, 4).Value = 67500;
            ws.Cell(4, 1).Value = "NO-EXISTE";
            ws.Cell(4, 3).Value = "1.000,50";
            ws.Cell(4, 4).Value = 1;
            ws.Cell(5, 1).Value = "CA-009";
            ws.Cell(5, 3).Value = "773068.73";
            ws.Cell(5, 4).Value = "82500";
            libro.SaveAs(ms);
        }
        ms.Position = 0;
        var hoja = ExcelDetalleLiquidacion.Leer(ms);
        Assert.Equal(new[] { "Item Id", "Valor", "Valor_2", "Cantidad" }, hoja.Columnas);
        Assert.Equal(3, hoja.Filas.Count);

        var items = new Dictionary<string, ItemStock>
        {
            ["CA-008"] = new("CA-008", "ELECTRICAL CABLE; FLAT; 2 AWG;", false),
            ["CA-009"] = new("CA-009", "ELECTRICAL CABLE; FLAT; 4 AWG;", false),
        };
        var (lista, mensajes) = ExcelDetalleLiquidacion.Convertir(hoja, 0, 3, 2, items);
        Assert.Equal(2, lista.Count);
        Assert.Equal(804644.65, lista[0].Valor);
        Assert.Equal(67500, lista[0].Cantidad);
        Assert.Equal("ELECTRICAL CABLE; FLAT; 2 AWG;", lista[0].Descripcion);
        Assert.Equal(773068.73, lista[1].Valor);
        Assert.Single(mensajes);
        Assert.Contains("NO-EXISTE", mensajes[0]);
    }

    [Fact]
    public void Reporte_con_el_layout_del_exe_y_el_nombre_de_la_empresa()
    {
        var gastos = new List<GastoImportacion>
        {
            new() { Fecha = new DateTime(2026, 8, 6), Proveedor = "CPTDC FABRICA", Referencia = "INV-1", Descripcion = "FACTURA", Valor = 1000, EsGasto = false },
            new() { Fecha = new DateTime(2026, 8, 10), Proveedor = "AGENTE", Referencia = "001-001-9", Descripcion = "FLETE", Valor = 60 },
            new() { Fecha = new DateTime(2026, 8, 11), Proveedor = "SEGUROS", Referencia = "001-002-1", Descripcion = "SEGURO", Valor = 40 },
        };
        var items = new List<ItemLiquidacion>
        {
            new() { ItemId = "CA-008", Descripcion = "CABLE 2", Cantidad = 10, Valor = 400 },
            new() { ItemId = "CA-009", Descripcion = "CABLE 4", Cantidad = 20, Valor = 600 },
        };
        Liquidaciones.Prorratear(items, 1000, 100);
        var bytes = ReporteLiquidacion.Generar("SANCEV ELECTRICA INDUSTRIAL CIA. LTDA.",
            new CuentaImportacion("13803", "IMPORTACION 41-2026", false, 3, 1100, null, null), gastos, items);

        using var libro = new XLWorkbook(new MemoryStream(bytes));
        var ws = libro.Worksheet("13803");
        Assert.Equal("SANCEV ELECTRICA INDUSTRIAL CIA. LTDA.", ws.Cell("A1").GetString());
        Assert.Equal("LIQUIDACIÓN DE IMPORTACIÓN", ws.Cell("A2").GetString());
        Assert.Equal("IMPORTACION 41-2026", ws.Cell("D5").GetString());
        Assert.Equal("GASTOS Y COSTOS", ws.Cell("A8").GetString());
        // Solo los gastos en la primera tabla (filas 9 y 10), con K y M = H.
        Assert.Equal("FLETE", ws.Cell("D9").GetString());
        Assert.Equal("SEGURO", ws.Cell("D10").GetString());
        Assert.Equal("H9", ws.Cell("K9").FormulaA1);
        Assert.Equal("INVENTARIOS", ws.Cell("A12").GetString());
        // Ítems con la factura del exterior en B/C y el prorrateo en N–Q.
        Assert.Equal("CA-008", ws.Cell("A13").GetString());
        Assert.Equal("CPTDC FABRICA", ws.Cell("B13").GetString());
        Assert.Equal(40.0, ws.Cell("O13").GetDouble());
        Assert.Equal(440.0, ws.Cell("P13").GetDouble());
        Assert.Equal(44.0, ws.Cell("Q13").GetDouble());
        Assert.Equal("INVENTARIOS T", ws.Cell("F15").GetString());
        Assert.Equal("SUM(H13:H14)", ws.Cell("H15").FormulaA1);
        Assert.Equal("SUM(M9:M14)", ws.Cell("M15").FormulaA1);
        Assert.Equal("TOTALES", ws.Cell("F16").GetString());
        Assert.Equal("SUM(H9:H14)", ws.Cell("H16").FormulaA1);
        Assert.Equal("INVENTARIO", ws.Cell("D17").GetString());
        Assert.Equal("TOTAL", ws.Cell("D20").GetString());
        Assert.Equal(1100.0, items.Sum(x => x.CostoTotal), 2);
        Assert.Equal("SUM(H18:H19)", ws.Cell("H20").FormulaA1);
        Assert.Equal("REVISADO", ws.Cell("B24").GetString());
    }
}
