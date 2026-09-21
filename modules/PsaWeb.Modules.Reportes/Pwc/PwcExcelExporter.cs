using ClosedXML.Excel;
using PsaWeb.Modules.Reportes.Comun;

namespace PsaWeb.Modules.Reportes.Pwc;

/// <summary>
/// Excel del reporte PWC. Reproduce el layout de <c>ReportPWC</c> (Access): Georgia 8, encabezado naranja
/// <c>#FF6600</c> de 2 filas, bordes medios, importes en formato contable, columna N como fórmula
/// <c>K−L−M−O</c> y fila de totales. Diferencias intencionales (ver docs/PLAN-REPORTES-ACCESS.md):
/// <list type="bullet">
///   <item>fechas como <b>fecha real</b> (Access las escribía con <c>CStr</c>);</item>
///   <item>columnas opcionales según <see cref="ConfiguracionPwc"/>;</item>
///   <item>el resumen de retenciones es dinámico (una columna auxiliar por porcentaje encontrado, con su
///   rótulo correcto) y con fórmulas <c>SUM</c> vivas; se elimina la leyenda «COPIAR Y PEGAR FÓRMULA…»;</item>
///   <item>resumen por ciudad con valores (el cuadro GYE/UIO de Access sólo traía los rótulos);</item>
///   <item>totales de subtotal … monto a cobrar (Access sólo totalizaba el monto a cobrar).</item>
/// </list>
/// </summary>
public sealed class PwcExcelExporter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string FormatoMoneda = "_(\"$\"* #,##0.00_);_(\"$\"* \\(#,##0.00\\);_(\"$\"* \"-\"??_);_(@_)";
    private const string FormatoFecha = "dd/mm/yyyy";
    private static readonly XLColor Naranja = XLColor.FromHtml("#FF6600");

    private enum C
    {
        Empresa, Factura, Emision, Vence, Cliente, Orden, Ciudad, Anunciante,
        Subtotal, Iva, Total, RetRenta, RetIva, Descuentos, Cobrar, Direccion,
    }

    public string NombreArchivo(ConfiguracionPwc config, string empresa, DateOnly corte)
    {
        var baseNombre = $"CXC {config.Cobrador} {empresa}".Trim();
        foreach (var invalido in Path.GetInvalidFileNameChars())
        {
            baseNombre = baseNombre.Replace(invalido, '_');
        }
        return $"{baseNombre} {corte:yyyyMMdd}.xlsx";
    }

    /// <param name="descripcionFiltros">Texto libre de los filtros aplicados (se imprime bajo el título).</param>
    public byte[] Generar(
        ResultadoPwc resultado,
        ConfiguracionPwc config,
        string empresa,
        DateOnly corte,
        string? descripcionFiltros = null)
    {
        var columnas = ColumnasVisibles(config);
        int Col(C c) => columnas.IndexOf(c) + 1;
        bool Hay(C c) => columnas.Contains(c);

        var resumenRet = config.MostrarRetenciones ? resultado.ResumenRetenciones() : Array.Empty<ResumenRetencionPwc>();
        var resumenCiudad = resultado.ResumenPorCiudad();

        // Altura de los bloques de resumen (encabezado + líneas); la tabla empieza debajo.
        var altoResumen = Math.Max(resumenRet.Count > 0 ? resumenRet.Count + 1 : 0, resumenCiudad.Count + 1);
        const int filaBloques = 3;
        var filaEnc = Math.Max(6, filaBloques + altoResumen + 1);
        var filaDatos = filaEnc + 2;
        var n = resultado.Filas.Count;
        var ultimaFila = filaDatos + n - 1;
        var filaTotal = (n > 0 ? ultimaFila : filaDatos - 1) + 2;

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(NombreHoja(config));
        ws.Style.Font.FontName = "Georgia";
        ws.Style.Font.FontSize = 8;

        // --- título ---------------------------------------------------------------------------
        var titulo = string.IsNullOrWhiteSpace(config.Titulo) ? $"CXC {config.Cobrador}" : config.Titulo!.Trim();
        ws.Cell(1, 1).Value = string.IsNullOrWhiteSpace(empresa) ? titulo : $"{titulo} — {empresa}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 11;
        ws.Cell(2, 1).Value = $"Corte: {corte:dd/MM/yyyy}" + (string.IsNullOrWhiteSpace(descripcionFiltros) ? "" : $" · {descripcionFiltros}");

        // --- encabezado de la tabla (2 filas) -----------------------------------------------------
        var ultimaCol = columnas.Count;
        EscribirEncabezado(ws, config, columnas, filaEnc);

        // --- datos -----------------------------------------------------------------------------
        var buckets = resumenRet.Select(r => (r.Tipo, r.Porcentaje)).ToList();
        for (var i = 0; i < n; i++)
        {
            var f = resultado.Filas[i];
            var r = filaDatos + i;
            if (Hay(C.Empresa)) ws.Cell(r, Col(C.Empresa)).Value = empresa;
            ws.Cell(r, Col(C.Factura)).Value = f.Factura;
            ws.Cell(r, Col(C.Emision)).Value = f.Emision.ToDateTime(TimeOnly.MinValue);
            if (f.Vence is { } v) ws.Cell(r, Col(C.Vence)).Value = v.ToDateTime(TimeOnly.MinValue);
            ws.Cell(r, Col(C.Cliente)).Value = f.Cliente;
            // Texto opcional: si está vacío la celda queda realmente en blanco (los resúmenes por ciudad lo cuentan así).
            if (Hay(C.Orden)) Texto(ws.Cell(r, Col(C.Orden)), f.Orden);
            if (Hay(C.Ciudad)) Texto(ws.Cell(r, Col(C.Ciudad)), f.Ciudad);
            if (Hay(C.Anunciante)) Texto(ws.Cell(r, Col(C.Anunciante)), f.Anunciante);
            ws.Cell(r, Col(C.Subtotal)).Value = f.Subtotal;
            ws.Cell(r, Col(C.Iva)).Value = f.Iva;
            ws.Cell(r, Col(C.Total)).Value = f.Total;
            if (config.MostrarRetenciones)
            {
                ws.Cell(r, Col(C.RetRenta)).Value = f.RetRenta;
                ws.Cell(r, Col(C.RetIva)).Value = f.RetIva;
                // N = K − L − M − O (Access: FormulaR1C1 "=RC[-3]-RC[-2]-RC[-1]-RC[1]")
                ws.Cell(r, Col(C.Descuentos)).FormulaA1 =
                    $"{Letra(Col(C.Total))}{r}-{Letra(Col(C.RetRenta))}{r}-{Letra(Col(C.RetIva))}{r}-{Letra(Col(C.Cobrar))}{r}";
            }
            ws.Cell(r, Col(C.Cobrar)).Value = f.MontoCobrar;
            if (Hay(C.Direccion)) Texto(ws.Cell(r, Col(C.Direccion)), f.Direccion);

            // Columnas auxiliares del resumen de retenciones (Access: V:Y).
            for (var b = 0; b < buckets.Count; b++)
            {
                var monto = f.RetencionRentaGanadora is { } rr && rr.Tipo == buckets[b].Tipo && rr.Porcentaje == buckets[b].Porcentaje
                    ? rr.Monto
                    : f.RetencionIvaGanadora is { } ri && ri.Tipo == buckets[b].Tipo && ri.Porcentaje == buckets[b].Porcentaje
                        ? ri.Monto
                        : (decimal?)null;
                if (monto is { } m)
                {
                    ws.Cell(r, ultimaCol + 2 + b).Value = m;
                }
            }
        }

        // --- formatos y bordes del cuerpo ---------------------------------------------------------------
        if (n > 0)
        {
            ws.Range(filaDatos, Col(C.Emision), ultimaFila, Col(C.Vence)).Style.DateFormat.Format = FormatoFecha;
            if (Hay(C.Orden)) ws.Range(filaDatos, Col(C.Orden), ultimaFila, Col(C.Orden)).Style.NumberFormat.Format = "@";
            ws.Range(filaDatos, Col(C.Subtotal), ultimaFila, Col(C.Cobrar)).Style.NumberFormat.Format = FormatoMoneda;

            var cuerpo = ws.Range(filaDatos, 1, ultimaFila, ultimaCol - (Hay(C.Direccion) ? 1 : 0));
            cuerpo.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
            cuerpo.Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);
        }

        // --- totales -----------------------------------------------------------------------------
        var colClienteTotal = Col(C.Cliente);
        ws.Cell(filaTotal, colClienteTotal).Value = "TOTALES";
        ws.Cell(filaTotal, colClienteTotal).Style.Font.Bold = true;
        foreach (var c in new[] { C.Subtotal, C.Iva, C.Total, C.RetRenta, C.RetIva, C.Descuentos, C.Cobrar })
        {
            if (!Hay(c)) continue;
            var l = Letra(Col(c));
            var celda = ws.Cell(filaTotal, Col(c));
            if (n > 0) celda.FormulaA1 = $"SUM({l}{filaDatos}:{l}{ultimaFila})";
            else celda.Value = 0;
            celda.Style.NumberFormat.Format = FormatoMoneda;
            celda.Style.Font.Bold = true;
        }
        var totalCobrar = ws.Cell(filaTotal, Col(C.Cobrar));
        totalCobrar.Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);
        totalCobrar.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(filaTotal, Col(C.Cobrar) + 1).Value = $"MONTO A COBRAR POR {config.Cobrador}";
        ws.Cell(filaTotal, Col(C.Cobrar) + 1).Style.Font.Bold = true;

        // --- resumen por ciudad -----------------------------------------------------------------------
        var colCiudadBloque = Hay(C.Ciudad) ? Col(C.Ciudad) : Col(C.Cliente);
        EscribirResumenCiudad(ws, resumenCiudad, filaBloques, colCiudadBloque, filaDatos, ultimaFila, n, Hay(C.Ciudad) ? Col(C.Ciudad) : 0, Col(C.Cobrar));

        // --- resumen de retenciones + columnas auxiliares -------------------------------------------
        if (resumenRet.Count > 0)
        {
            var colBloque = Math.Max(Col(C.Total), colCiudadBloque + 3);
            ws.Range(filaBloques, colBloque, filaBloques, colBloque + 1).Merge();
            var cabBloque = ws.Cell(filaBloques, colBloque);
            cabBloque.Value = "RESUMEN DE RETENCIONES";
            EstiloEncabezado(ws.Range(filaBloques, colBloque, filaBloques, colBloque + 1));

            for (var b = 0; b < resumenRet.Count; b++)
            {
                var colAux = ultimaCol + 2 + b;
                var la = Letra(colAux);
                ws.Range(filaEnc, colAux, filaEnc + 1, colAux).Merge();
                ws.Cell(filaEnc, colAux).Value = resumenRet[b].EtiquetaColumna;
                EstiloEncabezado(ws.Range(filaEnc, colAux, filaEnc + 1, colAux));
                if (n > 0) ws.Range(filaDatos, colAux, ultimaFila, colAux).Style.NumberFormat.Format = FormatoMoneda;

                var fila = filaBloques + 1 + b;
                ws.Cell(fila, colBloque).Value = resumenRet[b].Etiqueta;
                var val = ws.Cell(fila, colBloque + 1);
                if (n > 0) val.FormulaA1 = $"SUM({la}{filaDatos}:{la}{ultimaFila})";
                else val.Value = 0;
                val.Style.NumberFormat.Format = FormatoMoneda;
                ws.Column(colAux).Width = 12;
            }
            ws.Range(filaBloques, colBloque, filaBloques + resumenRet.Count, colBloque + 1)
                .Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);
            ws.Range(filaBloques + 1, colBloque, filaBloques + resumenRet.Count, colBloque + 1)
                .Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
        }

        AnchosDeColumna(ws, columnas);
        ws.Row(filaEnc + 1).Height = 42.75;

        wb.CalculateMode = XLCalculateMode.Auto;
        wb.FullCalculationOnLoad = true;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ------------------------------------------------------------------------------------------------

    private static List<C> ColumnasVisibles(ConfiguracionPwc cfg)
    {
        var l = new List<C>();
        if (cfg.MostrarEmpresa) l.Add(C.Empresa);
        l.AddRange(new[] { C.Factura, C.Emision, C.Vence, C.Cliente });
        if (cfg.MostrarOrden) l.Add(C.Orden);
        if (cfg.MostrarCiudad) l.Add(C.Ciudad);
        if (cfg.MostrarAnunciante) l.Add(C.Anunciante);
        l.AddRange(new[] { C.Subtotal, C.Iva, C.Total });
        if (cfg.MostrarRetenciones) l.AddRange(new[] { C.RetRenta, C.RetIva, C.Descuentos });
        l.Add(C.Cobrar);
        if (cfg.MostrarDireccion) l.Add(C.Direccion);
        return l;
    }

    private static void EscribirEncabezado(IXLWorksheet ws, ConfiguracionPwc cfg, List<C> cols, int fila)
    {
        int Col(C c) => cols.IndexOf(c) + 1;
        bool Hay(C c) => cols.Contains(c);

        void Doble(C c, string texto)
        {
            ws.Range(fila, Col(c), fila + 1, Col(c)).Merge();
            ws.Cell(fila, Col(c)).Value = texto;
        }

        if (Hay(C.Empresa)) Doble(C.Empresa, cfg.EncabezadoRazonSocial);

        ws.Range(fila, Col(C.Factura), fila, Col(C.Vence)).Merge();
        ws.Cell(fila, Col(C.Factura)).Value = "FACTURA";
        ws.Cell(fila + 1, Col(C.Factura)).Value = "NUMERO";
        ws.Cell(fila + 1, Col(C.Emision)).Value = "FECHA DE EMISION";
        ws.Cell(fila + 1, Col(C.Vence)).Value = "FECHA DE COBRANZA";

        Doble(C.Cliente, "AGENCIA / CLIENTE DIRECTO");
        if (Hay(C.Orden)) Doble(C.Orden, "ORDEN");
        if (Hay(C.Ciudad)) Doble(C.Ciudad, "CIUDAD COBROS");
        if (Hay(C.Anunciante)) Doble(C.Anunciante, "ANUNCIANTE");
        Doble(C.Subtotal, "SUBTOTAL");
        Doble(C.Iva, "IVA");
        Doble(C.Total, "TOTAL");

        if (cfg.MostrarRetenciones)
        {
            ws.Range(fila, Col(C.RetRenta), fila, Col(C.RetIva)).Merge();
            ws.Cell(fila, Col(C.RetRenta)).Value = "MENOS RETENCIONES";
            ws.Cell(fila + 1, Col(C.RetRenta)).Value = "IR % (SI CORRESPONDE)";
            ws.Cell(fila + 1, Col(C.RetIva)).Value = "IVA % (SI CORRESPONDE)";
            Doble(C.Descuentos, "DESCUENTOS (N/C, COMISIONES)");
        }

        Doble(C.Cobrar, $"MONTO A COBRAR POR {cfg.Cobrador}");
        if (Hay(C.Direccion)) Doble(C.Direccion, "DIRECCION");

        var ultima = cols.Count - (Hay(C.Direccion) ? 1 : 0);
        EstiloEncabezado(ws.Range(fila, 1, fila + 1, ultima));
        if (Hay(C.Direccion)) EstiloEncabezado(ws.Range(fila, cols.Count, fila + 1, cols.Count));
    }

    private static void EstiloEncabezado(IXLRange rango)
    {
        rango.Style.Fill.PatternType = XLFillPatternValues.Solid;
        rango.Style.Fill.BackgroundColor = Naranja;
        rango.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        rango.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        rango.Style.Alignment.WrapText = true;
        rango.Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);
        rango.Style.Border.SetInsideBorder(XLBorderStyleValues.Medium);
    }

    private static void EscribirResumenCiudad(
        IXLWorksheet ws, IReadOnlyList<ResumenCiudadPwc> resumen, int filaBloques, int col,
        int filaDatos, int ultimaFila, int n, int colCiudadDatos, int colCobrar)
    {
        if (resumen.Count == 0)
        {
            return;
        }

        ws.Range(filaBloques, col, filaBloques, col + 2).Merge();
        ws.Cell(filaBloques, col).Value = "RESUMEN POR CIUDAD";
        EstiloEncabezado(ws.Range(filaBloques, col, filaBloques, col + 2));

        for (var i = 0; i < resumen.Count; i++)
        {
            var fila = filaBloques + 1 + i;
            var r = resumen[i];
            ws.Cell(fila, col).Value = r.Etiqueta;
            var cant = ws.Cell(fila, col + 1);
            var monto = ws.Cell(fila, col + 2);
            if (n > 0 && colCiudadDatos > 0)
            {
                var rangoCiudad = $"{Letra(colCiudadDatos)}{filaDatos}:{Letra(colCiudadDatos)}{ultimaFila}";
                var rangoMonto = $"{Letra(colCobrar)}{filaDatos}:{Letra(colCobrar)}{ultimaFila}";
                // SUMIF/COUNTIF con criterio "" (celdas vacías) no es fiable entre versiones de Excel:
                // para «(sin ciudad)» se usa el criterio "=".
                var criterio = r.Ciudad.Length == 0 ? "\"=\"" : $"\"{r.Ciudad.Replace("\"", "\"\"")}\"";
                cant.FormulaA1 = $"COUNTIF({rangoCiudad},{criterio})";
                monto.FormulaA1 = $"SUMIF({rangoCiudad},{criterio},{rangoMonto})";
            }
            else
            {
                cant.Value = r.Facturas;
                monto.Value = r.MontoCobrar;
            }
            monto.Style.NumberFormat.Format = FormatoMoneda;
        }

        var bloque = ws.Range(filaBloques, col, filaBloques + resumen.Count, col + 2);
        bloque.Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);
        ws.Range(filaBloques + 1, col, filaBloques + resumen.Count, col + 2).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
    }

    private static void AnchosDeColumna(IXLWorksheet ws, List<C> cols)
    {
        // Anchos de Access (MakingSheetFormat): A 12,13 · B:D 12 · E 19,38 · F:G 12 · H 17 · I:O 12.
        for (var i = 0; i < cols.Count; i++)
        {
            ws.Column(i + 1).Width = cols[i] switch
            {
                C.Empresa => 12.13,
                C.Cliente => 19.38,
                C.Anunciante => 17,
                C.Direccion => 24,
                _ => 12,
            };
        }
    }

    private static string NombreHoja(ConfiguracionPwc cfg)
    {
        var nombre = $"CXC {cfg.Cobrador}".Trim();
        foreach (var c in new[] { '[', ']', ':', '*', '?', '/', '\\' })
        {
            nombre = nombre.Replace(c, ' ');
        }
        return nombre.Length > 31 ? nombre[..31] : nombre.Length == 0 ? "CXC" : nombre;
    }

    private static void Texto(IXLCell celda, string valor)
    {
        if (valor.Length > 0)
        {
            celda.SetValue(valor);
        }
    }

    private static string Letra(int columna) => XLHelper.GetColumnLetterFromNumber(columna);
}
