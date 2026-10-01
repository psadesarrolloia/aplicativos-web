using ClosedXML.Excel;
using PsaWeb.Compras.Importaciones;

namespace PsaWeb.Modules.Compras.Importaciones;

/// <summary>
/// Reporte «Liquidación de importación» (port de <c>ExcelReports/ApportionImportsCPTDC</c> del `.exe`, EPPlus → ClosedXML) con el mismo
/// layout: encabezado, tabla de gastos y costos, tabla de inventarios con el prorrateo (columnas M–Q), totales, tabla de costo
/// unitario y firmas. C1: el título es el nombre de la empresa (el `.exe` ponía «CPTDC ECUADOR S.A.» fijo, también para SANCEV).
/// </summary>
public static class ReporteLiquidacion
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static string NombreArchivo(string cuenta) => $"Liquidacion-importacion-{cuenta}.xlsx";

    public static byte[] Generar(string empresa, CuentaImportacion cuenta, IReadOnlyList<GastoImportacion> gastos, IReadOnlyList<ItemLiquidacion> items)
    {
        using var libro = new XLWorkbook();
        var ws = libro.Worksheets.Add(Hoja(cuenta.Cuenta));
        var fila = 1;

        // BuildHead
        ws.Cell(fila++, 1).Value = empresa.ToUpperInvariant();
        ws.Range("A1:Q1").Merge();
        ws.Cell(fila++, 1).Value = "LIQUIDACIÓN DE IMPORTACIÓN";
        ws.Range("A2:Q2").Merge();
        fila += 2;
        ws.Cell(fila, 1).Value = "CÓDIGO DE IMPORTACIÓN:";
        ws.Cell(fila, 4).Value = cuenta.Descripcion;
        ws.Range(fila, 1, fila, 3).Merge();
        ws.Range("A1:A2").Style.Font.SetFontSize(16).Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        ws.Range(fila, 1, fila, 4).Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        fila++;

        // BuildHead1stHead
        var inicioCabeceraTabla1 = fila;
        ws.Cell(fila, 4).Value = "GASTOS DE";
        ws.Cell(fila, 7).Value = "VALOR";
        ws.Cell(fila++, 13).Value = "LIQUIDACIÓN";
        ws.Range(inicioCabeceraTabla1, 13, inicioCabeceraTabla1, 17).Merge();
        string[] titulos = ["FECHA", "PROVEEDORES", "N° FACTURAS", "IMPORTACIÓN", "ITEM", "Q", "UNIT", "VALOR"];
        for (var c = 0; c < titulos.Length; c++) ws.Cell(fila, c + 1).Value = titulos[c];
        ws.Cell(fila, 11).Value = "TOTAL";
        ws.Cell(fila, 13).Value = "CTOS Y GTS";
        ws.Cell(fila, 14).Value = "%";
        ws.Cell(fila, 15).Value = "PRORR";
        ws.Cell(fila, 16).Value = "TOTAL";
        ws.Cell(fila, 17).Value = "CTO/UN";
        ws.Range(inicioCabeceraTabla1, 1, fila, 17).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Font.SetBold();
        fila++;

        // Build1stTableHead
        var inicioCuerpo1 = fila;
        ws.Cell(fila, 1).Value = "GASTOS Y COSTOS";
        ws.Range(fila, 1, fila, 11).Merge().Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        fila++;

        // Build1stTableDetail: solo los gastos (isCost).
        var inicioDetalle1 = fila;
        foreach (var g in gastos.Where(x => x.EsGasto))
        {
            ws.Cell(fila, 1).Value = g.Fecha;
            ws.Cell(fila, 2).Value = g.Proveedor;
            ws.Cell(fila, 3).Value = g.Referencia;
            ws.Cell(fila, 4).Value = g.Descripcion;
            ws.Cell(fila, 8).Value = g.Valor;
            ws.Cell(fila, 11).FormulaA1 = $"H{fila}";
            ws.Cell(fila, 13).FormulaA1 = $"H{fila}";
            fila++;
        }
        ws.Range(inicioDetalle1, 1, fila, 1).Style.DateFormat.Format = "dd/MM/yyyy";
        fila++;

        // Build2ndTableHead
        ws.Cell(fila, 1).Value = "INVENTARIOS";
        ws.Range(fila, 1, fila, 11).Merge().Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        fila++;

        // Build2ndTableDetail: proveedor y nº de la factura del exterior en cada ítem.
        var inicioDetalle2 = fila;
        var factura = gastos.FirstOrDefault(x => !x.EsGasto);
        var n = 1;
        foreach (var x in items)
        {
            ws.Cell(fila, 1).Value = x.ItemId;
            if (factura is not null)
            {
                ws.Cell(fila, 2).Value = factura.Proveedor;
                ws.Cell(fila, 3).Value = factura.Referencia;
            }
            ws.Cell(fila, 4).Value = x.Descripcion;
            ws.Cell(fila, 5).Value = n++;
            ws.Cell(fila, 6).Value = x.Cantidad;
            ws.Cell(fila, 7).Value = x.ValorUnitario;
            ws.Cell(fila, 8).Value = x.Valor;
            ws.Cell(fila, 11).FormulaA1 = $"H{fila}";
            ws.Cell(fila, 14).Value = x.Porcentaje;
            ws.Cell(fila, 15).Value = x.Prorrateo;
            ws.Cell(fila, 16).Value = x.CostoTotal;
            ws.Cell(fila, 17).Value = x.CostoUnitario;
            fila++;
        }

        // Build2ndTableFoot
        var finDetalle2 = fila - 1;
        var inicioPie2 = fila;
        ws.Cell(fila, 6).Value = "INVENTARIOS T";
        ws.Cell(fila, 8).FormulaA1 = $"SUM(H{inicioDetalle2}:H{finDetalle2})";
        ws.Cell(fila, 11).FormulaA1 = $"SUM(K{inicioDetalle2}:K{finDetalle2})";
        ws.Cell(fila, 13).FormulaA1 = $"SUM(M{inicioDetalle1}:M{finDetalle2})";
        foreach (var col in new[] { "N", "O", "P", "Q" })
        {
            ws.Cell($"{col}{fila}").FormulaA1 = $"SUM({col}{inicioDetalle2}:{col}{finDetalle2})";
        }
        ws.Range(fila, 6, fila, 7).Merge().Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        fila++;
        ws.Cell(fila, 6).Value = "TOTALES";
        ws.Range(fila, 6, fila, 7).Merge().Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        ws.Cell(fila, 8).FormulaA1 = $"SUM(H{inicioDetalle1}:H{finDetalle2})";
        ws.Range(inicioPie2, 8, fila, 17).Style.Font.SetBold();
        fila++;

        // Build3rdTableHead
        var inicioCabecera3 = fila;
        string[] titulos3 = ["INVENTARIO", "ITEM", "Q", "V/U", "TOTAL"];
        for (var c = 0; c < titulos3.Length; c++) ws.Cell(fila, c + 4).Value = titulos3[c];
        ws.Range(fila, 4, fila, 8).Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        fila++;

        // Build3rdTableDetail
        var inicioDetalle3 = fila;
        n = 1;
        foreach (var x in items)
        {
            ws.Cell(fila, 4).Value = x.Descripcion;
            ws.Cell(fila, 5).Value = n++;
            ws.Cell(fila, 6).Value = x.Cantidad;
            ws.Cell(fila, 7).Value = x.CostoUnitario;
            ws.Cell(fila++, 8).Value = x.CostoTotal;
        }

        // Build3rdTableFoot
        var finDetalle3 = fila - 1;
        var inicioPie3 = fila;
        ws.Cell(fila, 4).Value = "TOTAL";
        ws.Cell(fila, 6).FormulaA1 = $"SUM(F{inicioDetalle3}:F{finDetalle3})";
        ws.Cell(fila, 7).FormulaA1 = $"SUM(G{inicioDetalle3}:G{finDetalle3})";
        ws.Cell(fila, 8).FormulaA1 = $"SUM(H{inicioDetalle3}:H{finDetalle3})";
        ws.Cell(fila, 4).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        ws.Range(fila, 4, fila, 8).Style.Font.SetBold();
        fila++;

        // BuildFoot: firmas.
        var revisado = fila + 3;
        var fecha = fila + 8;
        foreach (var (desde, hasta) in new[] { (2, 3), (13, 16) })
        {
            ws.Cell(revisado, desde).Value = "REVISADO";
            ws.Cell(fecha, desde).Value = "FECHA: ";
            ws.Range(revisado, desde, revisado, hasta).Merge().Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Range(revisado, desde, revisado, hasta).Style.Border.TopBorder = XLBorderStyleValues.Dashed;
            ws.Range(fecha, desde, fecha, hasta).Style.Border.TopBorder = XLBorderStyleValues.Dashed;
        }

        // Formatos numéricos.
        ws.Range(inicioDetalle1, 7, inicioPie2, 17).Style.NumberFormat.Format = "0.00";
        if (finDetalle3 >= inicioDetalle3) ws.Range(inicioDetalle3, 7, inicioPie3 - 1, 8).Style.NumberFormat.Format = "0.00";

        // Bordes.
        Bordes(ws.Range(inicioCabeceraTabla1, 1, inicioCabeceraTabla1 + 1, 11), ws.Range(inicioCuerpo1, 1, inicioPie2 - 1, 11), ws.Range(inicioCabeceraTabla1, 1, inicioPie2 - 1, 11));
        Todos(ws.Range(inicioPie2, 6, inicioPie2, 11), XLBorderStyleValues.Medium);
        Todos(ws.Range(inicioPie2 + 1, 6, inicioPie2 + 1, 8), XLBorderStyleValues.Medium);
        Bordes(ws.Range(inicioCabecera3, 4, inicioCabecera3, 8), ws.Range(inicioDetalle3, 4, Math.Max(inicioDetalle3, inicioPie3 - 1), 8), ws.Range(inicioCabecera3, 4, Math.Max(inicioCabecera3, inicioPie3 - 1), 8));
        Todos(ws.Range(inicioPie3, 4, inicioPie3, 8), XLBorderStyleValues.Medium);
        Bordes(ws.Range(inicioCabeceraTabla1, 13, inicioCabeceraTabla1 + 1, 17), ws.Range(inicioCuerpo1, 13, inicioPie2 - 1, 17), ws.Range(inicioCabeceraTabla1, 13, inicioPie2 - 1, 17));
        Todos(ws.Range(inicioPie2, 13, inicioPie2, 17), XLBorderStyleValues.Medium);

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        libro.SaveAs(ms);
        return ms.ToArray();
    }

    private static void Todos(IXLRange r, XLBorderStyleValues estilo)
    {
        r.Style.Border.TopBorder = estilo;
        r.Style.Border.RightBorder = estilo;
        r.Style.Border.BottomBorder = estilo;
        r.Style.Border.LeftBorder = estilo;
    }

    /// <summary><c>BuildBorders(head, body, edge)</c>: cabecera gruesa, cuerpo fino y contorno grueso.</summary>
    private static void Bordes(IXLRange cabecera, IXLRange cuerpo, IXLRange contorno)
    {
        Todos(cabecera, XLBorderStyleValues.Medium);
        Todos(cuerpo, XLBorderStyleValues.Thin);
        contorno.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
    }

    /// <summary>Nombre de hoja válido (la cuenta; Excel no admite algunos caracteres ni más de 31).</summary>
    private static string Hoja(string cuenta)
    {
        var s = new string(cuenta.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
        return s.Length == 0 ? "Liquidacion" : s.Length > 31 ? s[..31] : s;
    }
}
