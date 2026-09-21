using ClosedXML.Excel;
using PsaWeb.Modules.Reportes.Comisiones;
using PsaWeb.Modules.Reportes.Comun;

namespace PsaWeb.Reportes.Tests;

public class ComisionesTests
{
    private static readonly FiltroComisiones Rango = new(ReciboDesde: "5122", ReciboHasta: "5146");

    private static async Task<ResultadoComisiones> Muestra(FiltroComisiones? filtro = null)
        => await new SampleComisionesRepository().GenerarAsync(filtro ?? Rango);

    // --- filtro de recibos --------------------------------------------------------------------------

    [Fact]
    public void Bug_C2_el_rango_se_compara_como_texto_y_deja_entrar_el_513()
    {
        Assert.True(Rango.ReciboEnRango("5122"));
        Assert.True(Rango.ReciboEnRango("5146"));
        Assert.True(Rango.ReciboEnRango("513"));   // '513' >= '5122' y '513' <= '5146' como texto
        Assert.False(Rango.ReciboEnRango("5147"));
        Assert.False(Rango.ReciboEnRango("5121"));
    }

    [Fact]
    public void Con_RangoNumerico_el_513_queda_fuera()
    {
        var numerico = Rango with { RangoNumerico = true };
        Assert.False(numerico.ReciboEnRango("513"));
        Assert.True(numerico.ReciboEnRango("5130"));
        Assert.False(numerico.ReciboEnRango("ABC"));
    }

    [Theory]
    [InlineData("1", "001")]
    [InlineData("12", "012")]
    [InlineData("5122", "5122")]
    public void FormatearDesde_replica_Format_000_de_Access(string desde, string esperado)
        => Assert.Equal(esperado, FiltroComisiones.FormatearDesde(desde));

    [Fact]
    public void Con_texto_un_recibo_de_un_digito_cae_en_el_rango_12_a_99()
    {
        var f = new FiltroComisiones(ReciboDesde: "12", ReciboHasta: "99");
        Assert.True(f.ReciboEnRango("5"));                                   // '5' >= '012' y '5' <= '99'
        Assert.False((f with { RangoNumerico = true }).ReciboEnRango("5"));  // 5 < 12
    }

    [Fact]
    public void Validacion_del_filtro()
    {
        Assert.False(new FiltroComisiones().TieneAcotador);
        Assert.True(Rango.TieneAcotador);
        Assert.True(new FiltroComisiones(FechaReciboDesde: new(2026, 7, 1)).TieneAcotador);

        Assert.True(Rango.RangoRecibosValido);
        Assert.False(new FiltroComisiones(ReciboDesde: "abc", ReciboHasta: "5").RangoRecibosValido);
        Assert.False(new FiltroComisiones(ReciboDesde: "5122").RangoRecibosValido); // falta el «hasta»
        Assert.True(new FiltroComisiones().RangoRecibosValido);                    // sin rango: no aplica
        Assert.False(new FiltroComisiones(FechaReciboDesde: new(2026, 8, 1), FechaReciboHasta: new(2026, 7, 1)).RangoFechasValido);
    }

    // --- armado -----------------------------------------------------------------------------------

    [Fact]
    public async Task Agrupa_por_cliente_con_el_recibo_de_su_primera_fila_en_la_cabecera()
    {
        var r = await Muestra();

        Assert.Equal(new[] { "AGENCIA DEMO UNO S.A.", "CYEDE CIA. LTDA." }, r.Grupos.Select(g => g.Cliente).ToArray());
        Assert.Equal(new[] { "5122", "513" }, r.Grupos.Select(g => g.ReciboEncabezado).ToArray());
        Assert.Equal(new[] { 2, 1 }, r.Grupos.Select(g => g.Filas.Count).ToArray());
    }

    [Fact]
    public async Task Abono_heredado_es_pagado_de_la_factura_menos_cruce_menos_retencion()
    {
        var r = await Muestra();
        var f = r.Filas.ToDictionary(x => x.Factura);

        Assert.Equal(1026m, f["001-001-000000201"].Abono);  // 1.120 − 0 − 94
        Assert.Equal(1480m, f["001-001-000000202"].Abono);  // 2.300 − 550 − 270 (el recibo sólo aplicó 1.200)
        Assert.Equal(1065.60m, f["001-001-003661"].Abono);
        Assert.Equal(550m, f["001-001-000000202"].Cruce);   // valor absoluto
        Assert.Equal(270m, f["001-001-000000202"].Retencion);
        Assert.Equal(1000m, f["001-001-000000201"].Subtotal);
        Assert.Equal(120m, f["001-001-000000201"].Iva);
    }

    [Fact]
    public async Task Bug_C1_con_la_correccion_el_abono_es_lo_que_aplico_el_recibo()
    {
        var r = await Muestra(Rango with { AbonoPorRecibo = true });
        var f = r.Filas.ToDictionary(x => x.Factura);

        Assert.Equal(1026m, f["001-001-000000201"].Abono);
        Assert.Equal(1200m, f["001-001-000000202"].Abono);   // ≠ 1.480 del cálculo heredado
        Assert.Equal(1065.60m, f["001-001-003661"].Abono);
    }

    [Fact]
    public async Task Totales_y_filtros_de_la_muestra()
    {
        var r = await Muestra();
        Assert.Equal(3, r.Facturas);
        Assert.Equal(3960m, r.TotalSubtotal);
        Assert.Equal(3571.60m, r.TotalAbono);
        Assert.Equal(4495.20m, r.TotalFacturado);

        var sinAntiguo = await Muestra(Rango with { RangoNumerico = true });
        Assert.Equal(2, sinAntiguo.Facturas);

        var porCliente = await Muestra(Rango with { Cliente = "cyede" });
        Assert.Single(porCliente.Grupos);

        var porCiudad = await Muestra(Rango with { Ciudad = "gye" });
        Assert.Equal(2, porCiudad.Facturas);

        var porFecha = await Muestra(new FiltroComisiones(FechaReciboDesde: new(2013, 1, 1), FechaReciboHasta: new(2013, 12, 31)));
        Assert.Equal(new[] { "001-001-003661" }, porFecha.Filas.Select(x => x.Factura).ToArray());

        Assert.True((await Muestra(new FiltroComisiones(ReciboDesde: "9000", ReciboHasta: "9999"))).SinDatos);
    }

    [Fact]
    public void Describir_lista_los_filtros()
    {
        Assert.Equal("Sin filtros", new FiltroComisiones().Describir());
        var d = (Rango with { RangoNumerico = true, AbonoPorRecibo = true, Cliente = "x" }).Describir();
        Assert.Contains("Recibos 5122–5146 (numérico)", d);
        Assert.Contains("Cliente contiene «x»", d);
        Assert.Contains("Abono = importe aplicado por el recibo", d);
    }

    // --- Excel --------------------------------------------------------------------------------------

    private static async Task<XLWorkbook> Excel(
        ConfiguracionComisiones? cfg = null, FiltroComisiones? filtro = null, ResultadoComisiones? resultado = null)
    {
        var f = filtro ?? Rango;
        var r = resultado ?? await Muestra(f);
        return new XLWorkbook(new MemoryStream(new ComisionesExcelExporter().Generar(r, cfg ?? new ConfiguracionComisiones(), f)));
    }

    [Fact]
    public async Task El_excel_tiene_el_encabezado_y_la_estructura_de_Access()
    {
        using var wb = await Excel();
        var ws = wb.Worksheet(1);

        Assert.Equal(
            new[] { "CLIENTE / FACTURA", "FECHA", "V. FACT.", "IVA", "TOTAL", "RET.", "CRUCE", "ABONO", "SALDO", "RECIBO", "FECH REC" },
            Enumerable.Range(1, 11).Select(c => ws.Cell(1, c).GetString()).ToArray());

        // Fila 2: cliente + recibo de su 1ª fila bajo «FECHA» (X1). Filas 3-4: facturas. Fila 5: otro cliente.
        Assert.Equal("AGENCIA DEMO UNO S.A.", ws.Cell(2, 1).GetString());
        Assert.Equal(5122, ws.Cell(2, 2).Value.GetNumber());
        Assert.True(ws.Cell(2, 3).IsEmpty());
        Assert.Equal("001-001-000000201", ws.Cell(3, 1).GetString());
        Assert.Equal("CYEDE CIA. LTDA.", ws.Cell(5, 1).GetString());
        Assert.Equal("Arial", ws.Cell(3, 1).Style.Font.FontName);
        Assert.Equal(10, ws.Cell(3, 1).Style.Font.FontSize);
    }

    [Fact]
    public async Task Las_fechas_son_reales_y_los_importes_y_recibos_numericos()
    {
        using var wb = await Excel();
        var ws = wb.Worksheet(1);

        Assert.Equal(XLDataType.DateTime, ws.Cell(3, 2).DataType);
        Assert.Equal(new DateTime(2026, 6, 1), ws.Cell(3, 2).GetDateTime());
        Assert.Equal(new DateTime(2026, 7, 13), ws.Cell(3, 11).GetDateTime());
        Assert.Equal("dd/mm/yyyy", ws.Cell(3, 11).Style.DateFormat.Format);

        Assert.Equal(1000, ws.Cell(3, 3).Value.GetNumber());
        Assert.Equal(120, ws.Cell(3, 4).Value.GetNumber());
        Assert.Equal(1120, ws.Cell(3, 5).Value.GetNumber());
        Assert.Equal(94, ws.Cell(3, 6).Value.GetNumber());
        Assert.Equal(1026, ws.Cell(3, 8).Value.GetNumber());
        Assert.Equal(5122, ws.Cell(3, 10).Value.GetNumber());
        Assert.Contains("$", ws.Cell(3, 3).Style.NumberFormat.Format);
    }

    [Fact]
    public async Task Total_general_con_formulas_SUM_al_final()
    {
        using var wb = await Excel();
        var ws = wb.Worksheet(1);
        var total = 6 + 2;

        Assert.Equal("TOTAL GENERAL", ws.Cell(total, 1).GetString());
        Assert.StartsWith("SUM(C2:C6", ws.Cell(total, 3).FormulaA1);
        Assert.Equal(3960, ws.Cell(total, 3).Value.GetNumber(), 2);
        Assert.Equal(3571.60, ws.Cell(total, 8).Value.GetNumber(), 2);
    }

    [Fact]
    public async Task Columnas_opcionales_y_comentario_de_la_correccion_C1()
    {
        var cfg = new ConfiguracionComisiones { MostrarImporteRecibo = true, MostrarCiudad = true };
        using var wb = await Excel(cfg, Rango with { AbonoPorRecibo = true });
        var ws = wb.Worksheet(1);

        Assert.Equal("IMPORTE RECIBO", ws.Cell(1, 12).GetString());
        Assert.Equal("CIUDAD", ws.Cell(1, 13).GetString());
        Assert.Equal(1200, ws.Cell(4, 12).Value.GetNumber());       // importe aplicado por el recibo (positivo)
        Assert.Equal("GYE", ws.Cell(4, 13).GetString());
        Assert.Equal(1200, ws.Cell(4, 8).Value.GetNumber());        // abono corregido
        Assert.True(ws.Cell(1, 8).HasComment);
    }

    [Fact]
    public async Task Sin_datos_genera_el_archivo_solo_con_encabezado_y_totales_en_cero()
    {
        using var wb = await Excel(resultado: ResultadoComisiones.Vacio);
        var ws = wb.Worksheet(1);

        Assert.Equal("CLIENTE / FACTURA", ws.Cell(1, 1).GetString());
        Assert.Equal("TOTAL GENERAL", ws.Cell(3, 1).GetString());
        Assert.Equal(0, ws.Cell(3, 8).Value.GetNumber());
    }

    [Fact]
    public void Nombre_de_archivo()
    {
        var exp = new ComisionesExcelExporter();
        Assert.Equal("COMISIONES RADIO DEMO recibos 5122-5146.xlsx",
            exp.NombreArchivo("RADIO DEMO", Rango, new DateOnly(2026, 9, 21)));
        Assert.Equal("COMISIONES RADIO DEMO hasta 20260921.xlsx",
            exp.NombreArchivo("RADIO DEMO", new FiltroComisiones(FechaReciboDesde: new(2026, 7, 1)), new DateOnly(2026, 9, 21)));
    }
}
