using ClosedXML.Excel;
using PsaWeb.Modules.Kardex.Data;

namespace PsaWeb.Modules.Kardex.Export;

/// <summary>
/// Genera el Kardex en Excel. Port de <c>ExcelReports/InventoryCost.cs</c> de
/// <c>Sage50usIntegration</c> (mismo layout: un bloque de encabezado por ítem,
/// columnas Cuenta / ItemID / Nombre / Fecha / Referencia y los grupos
/// «Entradas» / «Salidas» / «Saldos» con Cant. / Costo U. / Costo T.), con la
/// identidad visual de PSA.
///
/// <para>El bloque «Saldos» se escribe como <b>fórmulas de saldo corrido</b>
/// (igual que el .exe): la fila <c>.INICIAL.</c> lleva los valores literales del
/// snapshot; el resto acumula <c>F+I(+L fila-1)</c> y <c>H+K(+N fila-1)</c>, con
/// el unitario <c>=IF(L=0,0,N/L)</c>. Por eso la columna «Saldos» del Excel puede
/// diferir del snapshot <c>MajorType 3</c> que muestra la pantalla.</para>
/// </summary>
public sealed class KardexExcelExporter
{
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string Firma = "PSA · Soluciones Inteligentes · Ecuador";

    private const string FormatoNumero = "#,##0.00";
    private const string FormatoFecha = "dd/MM/yyyy";

    // Paleta PSA (misma que el exportador de Cierre de Caja).
    internal static readonly XLColor Navy = XLColor.FromHtml("#163154");
    internal static readonly XLColor NavyTint = XLColor.FromHtml("#EDF1F6");
    private static readonly XLColor Magenta = XLColor.FromHtml("#B40046");
    private static readonly XLColor GrisEtiqueta = XLColor.FromHtml("#5A6472");
    private static readonly XLColor GrisFirma = XLColor.FromHtml("#9AA3AF");
    private static readonly XLColor Hairline = XLColor.FromHtml("#C3CAD4");

    // Columnas (1-indexado), igual que el .exe.
    private const int ColCuenta = 1;
    private const int ColItem = 2;
    private const int ColNombre = 3;
    private const int ColFecha = 4;
    private const int ColRef = 5;
    private const int ColEntradas = 6;  // 6,7,8
    private const int ColSalidas = 9;   // 9,10,11
    private const int ColSaldos = 12;   // 12,13,14
    private const int ColUltima = 14;

    public string NombreArchivo(DateOnly desde, DateOnly hasta) =>
        $"Kardex_{desde:yyyyMMdd}_{hasta:yyyyMMdd}.xlsx";

    public byte[] Generar(ResultadoKardex resultado, DateOnly desde, DateOnly hasta)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = "Kardex de inventarios";
        workbook.Properties.Author = "PSA Soluciones Inteligentes";
        workbook.Properties.Company = "PSA Soluciones Inteligentes";

        var ws = workbook.Worksheets.Add("Kardex");
        ws.ShowGridLines = false;
        ws.Column(ColCuenta).Width = 12;
        ws.Column(ColItem).Width = 16;
        ws.Column(ColNombre).Width = 42;
        ws.Column(ColFecha).Width = 12;
        ws.Column(ColRef).Width = 22;
        for (var c = ColEntradas; c <= ColUltima; c++)
        {
            ws.Column(c).Width = 13;
        }

        // ---- Banda de título -------------------------------------------------
        var titulo = ws.Range(1, 1, 1, ColUltima);
        titulo.Merge();
        ws.Cell(1, 1).Value = "KARDEX DE INVENTARIOS";
        titulo.Style.Fill.BackgroundColor = Navy;
        titulo.Style.Font.Bold = true;
        titulo.Style.Font.FontSize = 16;
        titulo.Style.Font.FontColor = XLColor.White;
        titulo.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        titulo.Style.Alignment.Indent = 1;
        titulo.Style.Border.BottomBorder = XLBorderStyleValues.Thick;
        titulo.Style.Border.BottomBorderColor = Magenta;
        ws.Row(1).Height = 30;
        ws.Row(2).Height = 6;

        var periodo = ws.Cell(3, 1);
        periodo.Value = $"Período: {desde:dd/MM/yyyy} – {hasta:dd/MM/yyyy}";
        periodo.Style.Font.FontColor = GrisEtiqueta;
        periodo.Style.Font.FontSize = 10;

        var row = 5;

        string? itemActual = null;
        var startHead = 0;
        var endHead = 0;
        var startData = 0;
        var primeraFilaItem = false;

        foreach (var fila in resultado.Filas)
        {
            if (!string.Equals(fila.ItemId, itemActual, StringComparison.Ordinal))
            {
                if (itemActual is not null)
                {
                    FormatearBloque(ws, startHead, endHead, startData, row - 1);
                    row += 2;
                }

                itemActual = fila.ItemId;
                startHead = row;
                EscribirEncabezado(ws, row);
                endHead = row + 1;
                row += 2;
                startData = row;
                primeraFilaItem = true;
            }

            ws.Cell(row, ColCuenta).Value = fila.CuentaGl;
            ws.Cell(row, ColItem).Value = fila.ItemId;
            if (primeraFilaItem)
            {
                ws.Cell(row, ColNombre).Value = fila.Nombre;
            }
            ws.Cell(row, ColFecha).Value = fila.Fecha.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, ColRef).Value = fila.Referencia;

            EscribirMovimiento(ws, row, ColEntradas, fila.Entrada);
            EscribirMovimiento(ws, row, ColSalidas, fila.Salida);

            // Saldos: literal en la fila inicial, fórmula de saldo corrido en el resto.
            if (fila.EsInicial)
            {
                ws.Cell(row, ColSaldos).Value = fila.Saldo.Cantidad;
                ws.Cell(row, ColSaldos + 1).Value = fila.Saldo.CostoUnitario;
                ws.Cell(row, ColSaldos + 2).Value = fila.Saldo.CostoTotal;
            }
            else if (primeraFilaItem)
            {
                // Primer renglón del ítem sin saldo inicial: sin acumulado previo.
                ws.Cell(row, ColSaldos).FormulaA1 = $"F{row}+I{row}";
                ws.Cell(row, ColSaldos + 1).FormulaA1 = $"IF(L{row}=0,0,N{row}/L{row})";
                ws.Cell(row, ColSaldos + 2).FormulaA1 = $"H{row}+K{row}";
            }
            else
            {
                ws.Cell(row, ColSaldos).FormulaA1 = $"F{row}+I{row}+L{row - 1}";
                ws.Cell(row, ColSaldos + 1).FormulaA1 = $"IF(L{row}=0,0,N{row}/L{row})";
                ws.Cell(row, ColSaldos + 2).FormulaA1 = $"H{row}+K{row}+N{row - 1}";
            }

            primeraFilaItem = false;
            row++;
        }

        if (itemActual is not null)
        {
            FormatearBloque(ws, startHead, endHead, startData, row - 1);
            row += 2;
            row = EscribirResumen(ws, resultado, row);
        }
        else
        {
            ws.Cell(5, 1).Value = "Sin movimientos para los ítems y el rango seleccionados.";
            ws.Cell(5, 1).Style.Font.FontColor = GrisEtiqueta;
        }

        // ---- Firma al pie --------------------------------------------------
        var pie = row + 2;
        ws.Range(pie, 1, pie, ColUltima).Style.Border.TopBorder = XLBorderStyleValues.Hair;
        ws.Range(pie, 1, pie, ColUltima).Style.Border.TopBorderColor = Hairline;
        var firma = ws.Range(pie + 1, 1, pie + 1, ColUltima);
        firma.Merge();
        ws.Cell(pie + 1, 1).Value = Firma;
        firma.Style.Font.FontSize = 8;
        firma.Style.Font.Italic = true;
        firma.Style.Font.FontColor = GrisFirma;

        ws.PageSetup.Footer.Left.AddText($"Generado {DateTime.Now:dd/MM/yyyy HH:mm}", XLHFOccurrence.AllPages);
        ws.PageSetup.Footer.Center.AddText(Firma, XLHFOccurrence.AllPages);
        ws.SheetView.FreezeRows(0);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void EscribirEncabezado(IXLWorksheet ws, int row)
    {
        foreach (var (col, texto) in new[] { (ColEntradas, "Entradas"), (ColSalidas, "Salidas"), (ColSaldos, "Saldos") })
        {
            var g = ws.Range(row, col, row, col + 2);
            g.Merge();
            ws.Cell(row, col).Value = texto;
            g.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        ws.Cell(row + 1, ColCuenta).Value = "Cuenta";
        ws.Cell(row + 1, ColItem).Value = "ItemID";
        ws.Cell(row + 1, ColNombre).Value = "Item Nombre";
        ws.Cell(row + 1, ColFecha).Value = "Fecha";
        ws.Cell(row + 1, ColRef).Value = "Referencia";
        for (var grupo = 0; grupo < 3; grupo++)
        {
            var b = ColEntradas + grupo * 3;
            ws.Cell(row + 1, b).Value = "Cant.";
            ws.Cell(row + 1, b + 1).Value = "Costo U.";
            ws.Cell(row + 1, b + 2).Value = "Costo T.";
        }
    }

    private static void EscribirMovimiento(IXLWorksheet ws, int row, int baseCol, MovimientoKardex mov)
    {
        if (!mov.TieneValor)
        {
            return;
        }
        ws.Cell(row, baseCol).Value = mov.Cantidad;
        ws.Cell(row, baseCol + 1).Value = mov.CostoUnitario;
        ws.Cell(row, baseCol + 2).Value = mov.CostoTotal;
    }

    private static void FormatearBloque(IXLWorksheet ws, int startHead, int endHead, int startData, int endData)
    {
        var encabezado = ws.Range(startHead, 1, endHead, ColUltima);
        encabezado.Style.Font.Bold = true;
        encabezado.Style.Font.FontColor = XLColor.White;
        encabezado.Style.Fill.BackgroundColor = Navy;
        encabezado.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        encabezado.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        encabezado.Style.Border.OutsideBorderColor = Navy;

        if (endData < startData)
        {
            return;
        }

        ws.Range(startData, ColFecha, endData, ColFecha).Style.DateFormat.Format = FormatoFecha;
        ws.Range(startData, ColEntradas, endData, ColUltima).Style.NumberFormat.Format = FormatoNumero;

        var datos = ws.Range(startData, 1, endData, ColUltima);
        datos.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        datos.Style.Border.OutsideBorderColor = Navy;

        ws.Range(startData, ColEntradas, endData, ColEntradas + 2).Style.Fill.BackgroundColor = NavyTint;
        ws.Range(startData, ColSaldos, endData, ColSaldos + 2).Style.Fill.BackgroundColor = NavyTint;
    }

    /// <summary>
    /// Bloque «RESUMEN»: saldo final (valor) por cuenta + total general. Usa
    /// <see cref="ResultadoKardex.ResumenPorCuenta"/>, que toma la última fila de
    /// cada ítem (ya en orden cronológico) como su saldo final.
    /// </summary>
    /// <returns>La fila siguiente al bloque escrito.</returns>
    private static int EscribirResumen(IXLWorksheet ws, ResultadoKardex resultado, int row)
    {
        var titulo = ws.Range(row, 1, row, 2);
        titulo.Merge();
        ws.Cell(row, 1).Value = "RESUMEN";
        titulo.Style.Font.Bold = true;
        titulo.Style.Font.FontColor = XLColor.White;
        titulo.Style.Fill.BackgroundColor = Navy;
        row++;

        ws.Cell(row, 1).Value = "Cuenta";
        ws.Cell(row, 2).Value = "Saldo final";
        ws.Range(row, 1, row, 2).Style.Font.Bold = true;
        row++;

        var inicioDatos = row;
        foreach (var r in resultado.ResumenPorCuenta())
        {
            ws.Cell(row, 1).Value = r.CuentaGl;
            ws.Cell(row, 2).Value = r.SaldoFinal;
            row++;
        }
        ws.Range(inicioDatos, 2, row - 1, 2).Style.NumberFormat.Format = FormatoNumero;

        ws.Cell(row, 1).Value = "TOTAL GENERAL";
        ws.Cell(row, 2).Value = resultado.TotalGeneral();
        ws.Range(row, 1, row, 2).Style.Font.Bold = true;
        ws.Cell(row, 2).Style.NumberFormat.Format = FormatoNumero;
        ws.Range(row, 1, row, 2).Style.Border.TopBorder = XLBorderStyleValues.Medium;
        ws.Range(row, 1, row, 2).Style.Border.TopBorderColor = Navy;
        row++;

        return row;
    }
}
