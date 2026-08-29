using ClosedXML.Excel;
using PsaWeb.Modules.CierreDeCaja.Data;

namespace PsaWeb.Modules.CierreDeCaja.Export;

/// <summary>
/// Genera el reporte «Cierre de Caja» en Excel, reproduciendo el formato del
/// ejecutable original (<c>ReceiptsReportRollerD</c>): encabezado, período,
/// detalle de cobros, resumen y línea de firma.
/// </summary>
public sealed class CierreExcelExporter
{
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string FormatoNumero = "#,##0.00";

    public string NombreArchivo(DateOnly desde, DateOnly hasta) =>
        $"Cierre_Caja_{desde:yyyyMMdd}_{hasta:yyyyMMdd}.xlsx";

    public byte[] Generar(ResultadoCierre resultado, DateOnly desde, DateOnly hasta)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = "Reporte de Cierre de Caja";
        workbook.Properties.Author = "Aplicativos web PSA";

        var ws = workbook.Worksheets.Add("Cierre de Caja");

        // 1. Encabezado
        ws.Range("A1:D1").Merge();
        ws.Cell("A1").Value = "CIERRE DE CAJA";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 16;
        ws.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // 2. Fechas
        ws.Cell("A3").Value = "Fecha:";
        ws.Cell("B3").Value = DateTime.Today;
        ws.Cell("B3").Style.DateFormat.Format = "dd/MM/yyyy";
        ws.Cell("A4").Value = "Período:";
        ws.Cell("B4").Value = $"{desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}";

        // 3. Detalle de cobros
        ws.Range("A6:B6").Merge();
        ws.Cell("A6").Value = "DETALLE DE COBROS";
        ws.Cell("A6").Style.Font.Bold = true;
        ws.Cell("A6").Style.Font.FontSize = 12;

        ws.Cell("A7").Value = "Recibo";
        ws.Cell("B7").Value = "Monto";
        var cabecera = ws.Range("A7:B7");
        cabecera.Style.Font.Bold = true;
        cabecera.Style.Fill.BackgroundColor = XLColor.LightGray;
        cabecera.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

        var fila = 8;
        foreach (var cobro in resultado.Cobros)
        {
            ws.Cell(fila, 1).Value = cobro.Tipo;
            ws.Cell(fila, 2).Value = cobro.Subtotal;
            ws.Cell(fila, 2).Style.NumberFormat.Format = FormatoNumero;
            fila++;
        }

        ws.Cell(fila, 1).Value = "TOTAL COBROS:";
        ws.Cell(fila, 1).Style.Font.Bold = true;
        ws.Cell(fila, 2).Value = resultado.TotalCobros;
        ws.Cell(fila, 2).Style.NumberFormat.Format = FormatoNumero;
        ws.Cell(fila, 2).Style.Font.Bold = true;
        ws.Range(fila, 1, fila, 2).Style.Border.TopBorder = XLBorderStyleValues.Double;

        // 4. Resumen de cierre
        var resumen = fila + 2;
        ws.Range(resumen, 1, resumen, 2).Merge();
        ws.Cell(resumen, 1).Value = "RESUMEN DE CIERRE";
        ws.Cell(resumen, 1).Style.Font.Bold = true;
        ws.Cell(resumen, 1).Style.Font.FontSize = 12;

        resumen++;
        ws.Cell(resumen, 1).Value = "Total Ventas:";
        ws.Cell(resumen, 2).Value = resultado.TotalVentas;
        ws.Cell(resumen, 2).Style.NumberFormat.Format = FormatoNumero;

        resumen++;
        ws.Cell(resumen, 1).Value = "Total Cobros:";
        ws.Cell(resumen, 2).Value = resultado.TotalCobros;
        ws.Cell(resumen, 2).Style.NumberFormat.Format = FormatoNumero;

        resumen++;
        ws.Cell(resumen, 1).Value = "Diferencia:";
        ws.Cell(resumen, 2).Value = resultado.Diferencia;
        ws.Cell(resumen, 2).Style.NumberFormat.Format = FormatoNumero;
        ws.Cell(resumen, 2).Style.Font.FontColor =
            resultado.Diferencia != 0m ? XLColor.Red : XLColor.Green;

        ws.Range(resumen - 3, 1, resumen, 2).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

        // 5. Firma
        var firma = resumen + 3;
        ws.Range(firma, 1, firma, 2).Merge();
        ws.Cell(firma, 1).Value = "Responsable: _________________________";

        ws.Column(1).Width = 24;
        ws.Column(2).Width = 16;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
