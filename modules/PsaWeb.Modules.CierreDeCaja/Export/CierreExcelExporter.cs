using ClosedXML.Excel;
using PsaWeb.Modules.CierreDeCaja.Data;

namespace PsaWeb.Modules.CierreDeCaja.Export;

/// <summary>
/// Genera el reporte «Cierre de Caja» en Excel: mismo contenido que el ejecutable
/// original, con la identidad visual de PSA (paleta de marca, encabezado, filas
/// alternadas) y una firma sutil al pie.
/// </summary>
public sealed class CierreExcelExporter
{
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string Firma = "PSA · Soluciones Inteligentes · Ecuador";

    private const string FormatoNumero = "#,##0.00";

    // Paleta PSA
    internal static readonly XLColor Navy = XLColor.FromHtml("#163154");
    internal static readonly XLColor Magenta = XLColor.FromHtml("#B40046");
    internal static readonly XLColor Ok = XLColor.FromHtml("#0F7D4F");
    internal static readonly XLColor Danger = XLColor.FromHtml("#C4362F");
    private static readonly XLColor NavyTint = XLColor.FromHtml("#EDF1F6");
    private static readonly XLColor GrisEtiqueta = XLColor.FromHtml("#5A6472");
    private static readonly XLColor GrisFirma = XLColor.FromHtml("#9AA3AF");
    private static readonly XLColor Hairline = XLColor.FromHtml("#C3CAD4");

    public string NombreArchivo(DateOnly desde, DateOnly hasta) =>
        $"Cierre_Caja_{desde:yyyyMMdd}_{hasta:yyyyMMdd}.xlsx";

    public byte[] Generar(ResultadoCierre resultado, DateOnly desde, DateOnly hasta)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = "Reporte de Cierre de Caja";
        workbook.Properties.Author = "PSA Soluciones Inteligentes";
        workbook.Properties.Company = "PSA Soluciones Inteligentes";

        var ws = workbook.Worksheets.Add("Cierre de Caja");
        ws.ShowGridLines = false;
        ws.Column(1).Width = 30;
        ws.Column(2).Width = 20;

        // ---- 1. Banda de título ------------------------------------------------
        var titulo = ws.Range("A1:B1");
        titulo.Merge();
        ws.Cell("A1").Value = "CIERRE DE CAJA";
        titulo.Style.Fill.BackgroundColor = Navy;
        titulo.Style.Font.Bold = true;
        titulo.Style.Font.FontSize = 18;
        titulo.Style.Font.FontColor = XLColor.White;
        titulo.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        titulo.Style.Alignment.Indent = 1;
        titulo.Style.Border.BottomBorder = XLBorderStyleValues.Thick;
        titulo.Style.Border.BottomBorderColor = Magenta;
        ws.Row(1).Height = 34;
        ws.Row(2).Height = 8;

        // ---- 2. Fechas -------------------------------------------------------
        Etiqueta(ws.Cell("A3"), "Fecha");
        ws.Cell("B3").Value = DateTime.Today;
        ws.Cell("B3").Style.DateFormat.Format = "dd/MM/yyyy";

        Etiqueta(ws.Cell("A4"), "Período");
        ws.Cell("B4").Value = $"{desde:dd/MM/yyyy} – {hasta:dd/MM/yyyy}";

        // ---- 3. Detalle de cobros -----------------------------------------
        ws.Cell("A6").Value = "DETALLE DE COBROS";
        ws.Cell("A6").Style.Font.Bold = true;
        ws.Cell("A6").Style.Font.FontSize = 12;
        ws.Cell("A6").Style.Font.FontColor = Navy;

        var encabezado = ws.Range("A7:B7");
        ws.Cell("A7").Value = "RECIBO";
        ws.Cell("B7").Value = "MONTO";
        encabezado.Style.Fill.BackgroundColor = Navy;
        encabezado.Style.Font.Bold = true;
        encabezado.Style.Font.FontColor = XLColor.White;
        ws.Cell("B7").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        var fila = 8;
        var alterna = false;
        foreach (var cobro in resultado.Cobros)
        {
            ws.Cell(fila, 1).Value = cobro.Tipo;
            ws.Cell(fila, 2).Value = cobro.Subtotal;
            ws.Cell(fila, 2).Style.NumberFormat.Format = FormatoNumero;
            ws.Cell(fila, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            if (alterna)
            {
                ws.Range(fila, 1, fila, 2).Style.Fill.BackgroundColor = NavyTint;
            }
            alterna = !alterna;
            fila++;
        }

        var total = ws.Range(fila, 1, fila, 2);
        ws.Cell(fila, 1).Value = "TOTAL COBROS";
        ws.Cell(fila, 2).Value = resultado.TotalCobros;
        ws.Cell(fila, 2).Style.NumberFormat.Format = FormatoNumero;
        ws.Cell(fila, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        total.Style.Font.Bold = true;
        total.Style.Border.TopBorder = XLBorderStyleValues.Medium;
        total.Style.Border.TopBorderColor = Navy;

        // ---- 4. Resumen de cierre --------------------------------------
        var resumen = fila + 2;
        ws.Cell(resumen, 1).Value = "RESUMEN DE CIERRE";
        ws.Cell(resumen, 1).Style.Font.Bold = true;
        ws.Cell(resumen, 1).Style.Font.FontSize = 12;
        ws.Cell(resumen, 1).Style.Font.FontColor = Magenta;

        FilaResumen(ws, resumen + 1, "Total ventas", resultado.TotalVentas);
        FilaResumen(ws, resumen + 2, "Total cobros", resultado.TotalCobros);
        FilaResumen(ws, resumen + 3, "Diferencia", resultado.Diferencia,
            resultado.Diferencia == 0m ? Ok : Danger, bold: true);

        var caja = ws.Range(resumen + 1, 1, resumen + 3, 2);
        caja.Style.Fill.BackgroundColor = NavyTint;
        caja.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        caja.Style.Border.OutsideBorderColor = Navy;

        // ---- 5. Firma / marca al pie ---------------------------------
        var pie = resumen + 7;
        ws.Range(pie - 1, 1, pie - 1, 2).Style.Border.TopBorder = XLBorderStyleValues.Hair;
        ws.Range(pie - 1, 1, pie - 1, 2).Style.Border.TopBorderColor = Hairline;

        var firma = ws.Range(pie, 1, pie, 2);
        firma.Merge();
        ws.Cell(pie, 1).Value = Firma;
        firma.Style.Font.FontSize = 8;
        firma.Style.Font.Italic = true;
        firma.Style.Font.FontColor = GrisFirma;
        firma.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // Pie de página impreso (aparece en cada hoja al imprimir / exportar a PDF).
        ws.PageSetup.Footer.Left.AddText($"Generado {DateTime.Now:dd/MM/yyyy HH:mm}", XLHFOccurrence.AllPages);
        ws.PageSetup.Footer.Center.AddText(Firma, XLHFOccurrence.AllPages);
        ws.PageSetup.Margins.Top = 0.6;
        ws.PageSetup.Margins.Bottom = 0.8;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Etiqueta(IXLCell cell, string texto)
    {
        cell.Value = texto;
        cell.Style.Font.FontColor = GrisEtiqueta;
        cell.Style.Font.FontSize = 10;
    }

    private static void FilaResumen(IXLWorksheet ws, int fila, string etiqueta, decimal valor,
        XLColor? colorValor = null, bool bold = false)
    {
        ws.Cell(fila, 1).Value = etiqueta;
        ws.Cell(fila, 1).Style.Font.Bold = bold;

        var celda = ws.Cell(fila, 2);
        celda.Value = valor;
        celda.Style.NumberFormat.Format = FormatoNumero;
        celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        celda.Style.Font.Bold = bold;
        if (colorValor is not null)
        {
            celda.Style.Font.FontColor = colorValor;
        }
    }
}
