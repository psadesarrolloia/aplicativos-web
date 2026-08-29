using ClosedXML.Excel;
using PsaWeb.Modules.CierreDeCaja.Data;
using PsaWeb.Modules.CierreDeCaja.Export;

namespace PsaWeb.CierreDeCaja.Tests;

public class CierreExcelExporterTests
{
    private static readonly DateOnly Desde = new(2026, 8, 1);
    private static readonly DateOnly Hasta = new(2026, 8, 31);

    private static ResultadoCierre Muestra() => new(
        new[]
        {
            new CobroPorTipo("Efectivo", 100m),
            new CobroPorTipo("Cheque", 50m),
        },
        TotalCobros: 150m,
        TotalVentas: 175m);

    private static IXLWorksheet Abrir(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return new XLWorkbook(ms).Worksheet(1);
    }

    [Fact]
    public void Genera_un_xlsx_valido_con_el_encabezado()
    {
        var bytes = new CierreExcelExporter().Generar(Muestra(), Desde, Hasta);

        Assert.NotEmpty(bytes);
        var ws = Abrir(bytes);
        Assert.Equal("CIERRE DE CAJA", ws.Cell("A1").GetString());
        Assert.Equal("01/08/2026 al 31/08/2026", ws.Cell("B4").GetString());
    }

    [Fact]
    public void Incluye_el_detalle_y_el_total_de_cobros()
    {
        var ws = Abrir(new CierreExcelExporter().Generar(Muestra(), Desde, Hasta));

        Assert.Equal("Efectivo", ws.Cell("A8").GetString());
        Assert.Equal(100m, ws.Cell("B8").GetValue<decimal>());
        Assert.Equal("Cheque", ws.Cell("A9").GetString());
        Assert.Equal("TOTAL COBROS:", ws.Cell("A10").GetString());
        Assert.Equal(150m, ws.Cell("B10").GetValue<decimal>());
    }

    [Fact]
    public void Resumen_muestra_diferencia_en_rojo_cuando_no_cuadra()
    {
        var ws = Abrir(new CierreExcelExporter().Generar(Muestra(), Desde, Hasta));

        // Fila 10 = TOTAL COBROS; resumen empieza en 12 (10 + 2).
        Assert.Equal("RESUMEN DE CIERRE", ws.Cell("A12").GetString());
        Assert.Equal("Diferencia:", ws.Cell("A15").GetString());
        Assert.Equal(25m, ws.Cell("B15").GetValue<decimal>());
        Assert.Equal(XLColor.Red, ws.Cell("B15").Style.Font.FontColor);
    }

    [Fact]
    public void NombreArchivo_usa_el_rango()
    {
        var nombre = new CierreExcelExporter().NombreArchivo(Desde, Hasta);

        Assert.Equal("Cierre_Caja_20260801_20260831.xlsx", nombre);
    }
}
