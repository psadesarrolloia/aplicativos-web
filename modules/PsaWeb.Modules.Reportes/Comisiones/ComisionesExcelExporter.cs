using System.Globalization;
using ClosedXML.Excel;
using PsaWeb.Modules.Reportes.Comun;

namespace PsaWeb.Modules.Reportes.Comisiones;

/// <summary>
/// Excel del reporte de Comisiones. Reproduce el de <c>ReportpComissions</c> (Access): fila 1 de encabezado
/// (<c>CLIENTE / FACTURA · FECHA · V. FACT. · IVA · TOTAL · RET. · CRUCE · ABONO · SALDO · RECIBO · FECH REC</c>),
/// una fila de cabecera por cliente (nombre + n.º de su primer recibo, artefacto X1 del original) y una fila por
/// factura, en Arial 10 y con formato de moneda en C:I. Diferencias intencionales:
/// <list type="bullet">
///   <item>fechas como <b>fecha real</b> (Access las escribía con <c>CStr</c>) e importes como número;</item>
///   <item>columnas opcionales (importe aplicado por el recibo, ciudad);</item>
///   <item>fila de <b>TOTAL GENERAL</b> al final (fórmulas <c>SUM</c>) — el original no totalizaba;</item>
///   <item>si se exporta con la corrección C1 (abono = importe del recibo) la celda H1 lo dice en un comentario.</item>
/// </list>
/// </summary>
public sealed class ComisionesExcelExporter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string FormatoMoneda = "_(\"$\"* #,##0.00_);_(\"$\"* \\(#,##0.00\\);_(\"$\"* \"-\"??_);_(@_)";
    private const string FormatoFecha = "dd/mm/yyyy";

    public string NombreArchivo(string empresa, FiltroComisiones filtro, DateOnly corte)
    {
        var rango = filtro.TieneRangoRecibos
            ? $"recibos {filtro.ReciboDesde!.Trim()}-{filtro.ReciboHasta!.Trim()}"
            : $"hasta {corte:yyyyMMdd}";
        var nombre = $"COMISIONES {empresa} {rango}".Trim();
        foreach (var invalido in Path.GetInvalidFileNameChars())
        {
            nombre = nombre.Replace(invalido, '_');
        }
        return nombre + ".xlsx";
    }

    public byte[] Generar(
        ResultadoComisiones resultado,
        ConfiguracionComisiones config,
        FiltroComisiones filtro)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(string.IsNullOrWhiteSpace(config.Titulo) ? "Comisiones" : NombreHoja(config.Titulo!));
        ws.Style.Font.FontName = "Arial";
        ws.Style.Font.FontSize = 10;

        var encabezados = new List<string>
        {
            "CLIENTE / FACTURA", "FECHA", "V. FACT.", "IVA", "TOTAL", "RET.", "CRUCE", "ABONO", "SALDO", "RECIBO", "FECH REC",
        };
        var colImporteRecibo = 0;
        var colCiudad = 0;
        if (config.MostrarImporteRecibo)
        {
            encabezados.Add("IMPORTE RECIBO");
            colImporteRecibo = encabezados.Count;
        }
        if (config.MostrarCiudad)
        {
            encabezados.Add("CIUDAD");
            colCiudad = encabezados.Count;
        }

        for (var i = 0; i < encabezados.Count; i++)
        {
            ws.Cell(1, i + 1).Value = encabezados[i];
        }
        var cabecera = ws.Range(1, 1, 1, encabezados.Count);
        cabecera.Style.Font.Bold = true;
        cabecera.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
        if (filtro.AbonoPorRecibo)
        {
            ws.Cell(1, 8).GetComment().AddText(
                "ABONO = importe que aplicó ese recibo a la factura (corrección del bug C1 del reporte de Access).");
        }

        var fila = 1;
        foreach (var g in resultado.Grupos)
        {
            fila++;
            ws.Cell(fila, 1).SetValue(g.Cliente);
            ws.Cell(fila, 1).Style.Font.Bold = true;
            EscribirRecibo(ws.Cell(fila, 2), g.ReciboEncabezado); // X1: en Access, n.º de recibo bajo «FECHA»

            foreach (var f in g.Filas)
            {
                fila++;
                ws.Cell(fila, 1).SetValue(f.Factura);
                ws.Cell(fila, 2).Value = f.Fecha.ToDateTime(TimeOnly.MinValue);
                ws.Cell(fila, 2).Style.DateFormat.Format = FormatoFecha;
                ws.Cell(fila, 3).Value = f.Subtotal;
                ws.Cell(fila, 4).Value = f.Iva;
                if (f.Total is { } t) ws.Cell(fila, 5).Value = t;
                ws.Cell(fila, 6).Value = f.Retencion;
                ws.Cell(fila, 7).Value = f.Cruce;
                ws.Cell(fila, 8).Value = f.Abono;
                if (f.Saldo is { } s) ws.Cell(fila, 9).Value = s;
                EscribirRecibo(ws.Cell(fila, 10), f.Recibo);
                ws.Cell(fila, 11).Value = f.FechaRecibo.ToDateTime(TimeOnly.MinValue);
                ws.Cell(fila, 11).Style.DateFormat.Format = FormatoFecha;
                if (colImporteRecibo > 0)
                {
                    ws.Cell(fila, colImporteRecibo).Value = Math.Abs(f.ImporteRecibo);
                }
                if (colCiudad > 0 && f.Ciudad.Length > 0)
                {
                    ws.Cell(fila, colCiudad).SetValue(f.Ciudad);
                }
            }
        }

        var ultimaFila = fila;
        if (ultimaFila > 1)
        {
            ws.Range(2, 3, ultimaFila, 9).Style.NumberFormat.Format = FormatoMoneda;
            if (colImporteRecibo > 0)
            {
                ws.Range(2, colImporteRecibo, ultimaFila, colImporteRecibo).Style.NumberFormat.Format = FormatoMoneda;
            }
        }

        // TOTAL GENERAL (nuevo): una fila en blanco y las sumas de C:I.
        var filaTotal = ultimaFila + 2;
        ws.Cell(filaTotal, 1).Value = "TOTAL GENERAL";
        ws.Cell(filaTotal, 1).Style.Font.Bold = true;
        for (var col = 3; col <= 9; col++)
        {
            var letra = XLHelper.GetColumnLetterFromNumber(col);
            var celda = ws.Cell(filaTotal, col);
            if (ultimaFila > 1) celda.FormulaA1 = $"SUM({letra}2:{letra}{ultimaFila})";
            else celda.Value = 0;
            celda.Style.NumberFormat.Format = FormatoMoneda;
            celda.Style.Font.Bold = true;
        }

        try
        {
            ws.Columns(1, encabezados.Count).AdjustToContents(); // Access: Columns("A:K").EntireColumn.AutoFit
        }
        catch (Exception)
        {
            // Sin motor de fuentes disponible: anchos fijos razonables.
            ws.Column(1).Width = 46;
            for (var c = 2; c <= encabezados.Count; c++) ws.Column(c).Width = 14;
        }
        ws.SheetView.FreezeRows(1);

        wb.CalculateMode = XLCalculateMode.Auto;
        wb.FullCalculationOnLoad = true;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>Access escribía el recibo con <c>CStr</c> y Excel lo convertía a número: se conserva (5124 → número).</summary>
    private static void EscribirRecibo(IXLCell celda, string recibo)
    {
        if (recibo.Length > 0
            && recibo.All(char.IsAsciiDigit)
            && !(recibo.Length > 1 && recibo[0] == '0')
            && long.TryParse(recibo, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            celda.Value = n;
        }
        else if (recibo.Length > 0)
        {
            celda.SetValue(recibo);
        }
    }

    private static string NombreHoja(string titulo)
    {
        foreach (var c in new[] { '[', ']', ':', '*', '?', '/', '\\' })
        {
            titulo = titulo.Replace(c, ' ');
        }
        titulo = titulo.Trim();
        return titulo.Length == 0 ? "Comisiones" : titulo.Length > 31 ? titulo[..31] : titulo;
    }
}
