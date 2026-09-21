using ClosedXML.Excel;
using PsaWeb.Modules.Reportes.Comun;
using PsaWeb.Modules.Reportes.Pwc;

namespace PsaWeb.Reportes.Tests;

public class PwcExcelExporterTests
{
    private const string Empresa = "RADIO FM DEMO CIA. LTDA.";
    private static readonly DateOnly Corte = new(2026, 9, 21);

    private static async Task<ResultadoPwc> Muestra() => await new SamplePwcRepository().GenerarAsync(new FiltroPwc());

    private static async Task<(XLWorkbook Wb, IXLWorksheet Ws, int FilaEnc)> Generar(
        ConfiguracionPwc? cfg = null, ResultadoPwc? resultado = null)
    {
        var bytes = new PwcExcelExporter().Generar(resultado ?? await Muestra(), cfg ?? new ConfiguracionPwc(), Empresa, Corte, "Sin filtros");
        var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(1);
        var fila = Enumerable.Range(1, 40).First(r => ws.Cell(r, 1).GetString() == "FACTURA" || ws.Cell(r, 2).GetString() == "FACTURA");
        return (wb, ws, fila);
    }

    private static double Numero(IXLCell c) => c.Value.GetNumber();

    [Fact]
    public async Task El_encabezado_es_el_del_reporte_de_Access_con_las_mismas_letras_de_columna()
    {
        var (wb, ws, h) = await Generar();
        using var _ = wb;

        Assert.Equal("RADIO (RAZON SOCIAL)", ws.Cell(h, "A").GetString());
        Assert.Equal("FACTURA", ws.Cell(h, "B").GetString());
        Assert.Equal("NUMERO", ws.Cell(h + 1, "B").GetString());
        Assert.Equal("FECHA DE EMISION", ws.Cell(h + 1, "C").GetString());
        Assert.Equal("FECHA DE COBRANZA", ws.Cell(h + 1, "D").GetString());
        Assert.Equal("AGENCIA / CLIENTE DIRECTO", ws.Cell(h, "E").GetString());
        Assert.Equal("ORDEN", ws.Cell(h, "F").GetString());
        Assert.Equal("CIUDAD COBROS", ws.Cell(h, "G").GetString());
        Assert.Equal("ANUNCIANTE", ws.Cell(h, "H").GetString());
        Assert.Equal("SUBTOTAL", ws.Cell(h, "I").GetString());
        Assert.Equal("IVA", ws.Cell(h, "J").GetString());
        Assert.Equal("TOTAL", ws.Cell(h, "K").GetString());
        Assert.Equal("MENOS RETENCIONES", ws.Cell(h, "L").GetString());
        Assert.Equal("IR % (SI CORRESPONDE)", ws.Cell(h + 1, "L").GetString());
        Assert.Equal("IVA % (SI CORRESPONDE)", ws.Cell(h + 1, "M").GetString());
        Assert.Equal("DESCUENTOS (N/C, COMISIONES)", ws.Cell(h, "N").GetString());
        Assert.Equal("MONTO A COBRAR POR PWC", ws.Cell(h, "O").GetString());

        var merges = ws.MergedRanges.Select(m => m.RangeAddress.ToStringRelative()).ToHashSet();
        Assert.Contains($"B{h}:D{h}", merges);
        Assert.Contains($"L{h}:M{h}", merges);
        Assert.Contains($"A{h}:A{h + 1}", merges);
        Assert.Contains($"O{h}:O{h + 1}", merges);
    }

    [Fact]
    public async Task Estilo_de_Access_Georgia_8_y_encabezado_naranja()
    {
        var (wb, ws, h) = await Generar();
        using var _ = wb;

        Assert.Equal("Georgia", ws.Cell(h + 2, "B").Style.Font.FontName);
        Assert.Equal(8, ws.Cell(h + 2, "B").Style.Font.FontSize);
        Assert.Equal("FFFF6600", ws.Cell(h, "B").Style.Fill.BackgroundColor.Color.Name.ToUpperInvariant());
        Assert.Equal(42.75, ws.Row(h + 1).Height, 2);
    }

    [Fact]
    public async Task Las_fechas_son_fechas_reales_no_texto()
    {
        var (wb, ws, h) = await Generar();
        using var _ = wb;

        var emision = ws.Cell(h + 2, "C");
        var cobranza = ws.Cell(h + 2, "D");
        Assert.Equal(XLDataType.DateTime, emision.DataType);
        Assert.Equal(new DateTime(2026, 8, 3), emision.GetDateTime());
        Assert.Equal(new DateTime(2026, 9, 2), cobranza.GetDateTime());
        Assert.Equal("dd/mm/yyyy", emision.Style.DateFormat.Format);
    }

    [Fact]
    public async Task Los_importes_son_numeros_y_la_columna_N_es_la_formula_K_menos_L_menos_M_menos_O()
    {
        var (wb, ws, h) = await Generar();
        using var _ = wb;
        var r = h + 2; // 1ª factura: 001-001-000000101

        Assert.Equal(1000, Numero(ws.Cell(r, "I")));
        Assert.Equal(120, Numero(ws.Cell(r, "J")));
        Assert.Equal(1120, Numero(ws.Cell(r, "K")));
        Assert.Equal(10, Numero(ws.Cell(r, "L")));
        Assert.Equal(84, Numero(ws.Cell(r, "M")));
        Assert.Equal(1026, Numero(ws.Cell(r, "O")));
        Assert.Equal($"K{r}-L{r}-M{r}-O{r}", ws.Cell(r, "N").FormulaA1.TrimStart('='));

        // N = 1120 − 10 − 84 − 1026 = 0; en la 2ª factura = 500 (pagos aplicados).
        Assert.Equal(0, Numero(ws.Cell(r, "N")), 2);
        Assert.Equal(500, Numero(ws.Cell(r + 1, "N")), 2);
    }

    [Fact]
    public async Task Orden_se_guarda_como_texto_y_la_ciudad_vacia_queda_en_blanco()
    {
        var (wb, ws, h) = await Generar();
        using var _ = wb;

        Assert.Equal(XLDataType.Text, ws.Cell(h + 2, "F").DataType);
        Assert.Equal("@", ws.Cell(h + 2, "F").Style.NumberFormat.Format);
        Assert.True(ws.Cell(h + 4, "G").IsEmpty()); // 001-001-000000103 no tiene ciudad
    }

    [Fact]
    public async Task La_fila_de_totales_suma_cada_columna_de_importe()
    {
        var (wb, ws, h) = await Generar();
        using var _ = wb;
        var total = h + 2 + 4 + 1; // datos (4) + 1 fila en blanco

        Assert.Equal("TOTALES", ws.Cell(total, "E").GetString());
        Assert.Equal(5600, Numero(ws.Cell(total, "K")), 2);
        Assert.Equal(96.25, Numero(ws.Cell(total, "L")), 2);
        Assert.Equal(432, Numero(ws.Cell(total, "M")), 2);
        Assert.Equal(500, Numero(ws.Cell(total, "N")), 2);
        Assert.Equal(4571.75, Numero(ws.Cell(total, "O")), 2);
        Assert.Equal("MONTO A COBRAR POR PWC", ws.Cell(total, "P").GetString());
    }

    [Fact]
    public async Task El_resumen_de_retenciones_es_dinamico_y_usa_formulas_SUM_sobre_columnas_auxiliares()
    {
        var (wb, ws, h) = await Generar();
        using var _ = wb;

        // Bloque en las filas 4..: etiqueta en la columna K (TOTAL) y valor en L.
        var filas = Enumerable.Range(3, h - 4).Select(r => (Etiqueta: ws.Cell(r, "K").GetString(), Celda: ws.Cell(r, "L"))).ToList();
        Assert.Equal("RESUMEN DE RETENCIONES", filas[0].Etiqueta);

        var esperado = new Dictionary<string, double>
        {
            ["RET. IR 1%"] = 10, ["RET. IR 1,75%"] = 26.25, ["RET. IR 3%"] = 60,
            ["RET. IVA 70%"] = 252, ["RET. IVA (sin %)"] = 180,
        };
        foreach (var (etiqueta, monto) in esperado)
        {
            var celda = filas.Single(f => f.Etiqueta == etiqueta).Celda;
            Assert.StartsWith("SUM(", celda.FormulaA1);
            Assert.Equal(monto, Numero(celda), 2);
        }
    }

    [Fact]
    public async Task El_resumen_por_ciudad_cuenta_facturas_y_suma_el_monto_a_cobrar()
    {
        var (wb, ws, h) = await Generar();
        using var _ = wb;

        // Bloque en G..I desde la fila 3.
        Assert.Equal("RESUMEN POR CIUDAD", ws.Cell(3, "G").GetString());
        Assert.Equal("GYE", ws.Cell(4, "G").GetString());
        Assert.Equal(2, Numero(ws.Cell(4, "H")));
        Assert.Equal(2499.75, Numero(ws.Cell(4, "I")), 2);
        Assert.Equal("UIO", ws.Cell(5, "G").GetString());
        Assert.Equal(1512, Numero(ws.Cell(5, "I")), 2);
        Assert.Equal("(sin ciudad)", ws.Cell(6, "G").GetString());
        Assert.Contains("COUNTIF(", ws.Cell(6, "H").FormulaA1);
    }

    [Fact]
    public async Task Las_columnas_opcionales_se_quitan_y_el_resto_se_corre()
    {
        var cfg = new ConfiguracionPwc
        {
            MostrarEmpresa = false, MostrarOrden = false, MostrarAnunciante = false, MostrarRetenciones = false,
        };
        var (wb, ws, h) = await Generar(cfg);
        using var _ = wb;

        // Factura(A) Emisión(B) Cobranza(C) Cliente(D) Ciudad(E) Subtotal(F) IVA(G) Total(H) A cobrar(I)
        Assert.Equal("FACTURA", ws.Cell(h, "A").GetString());
        Assert.Equal("AGENCIA / CLIENTE DIRECTO", ws.Cell(h, "D").GetString());
        Assert.Equal("CIUDAD COBROS", ws.Cell(h, "E").GetString());
        Assert.Equal("SUBTOTAL", ws.Cell(h, "F").GetString());
        Assert.Equal("MONTO A COBRAR POR PWC", ws.Cell(h, "I").GetString());
        Assert.DoesNotContain(ws.CellsUsed(), c => c.GetString().Contains("MENOS RETENCIONES"));
        Assert.DoesNotContain(ws.CellsUsed(), c => c.GetString().StartsWith("RESUMEN DE RETENCIONES"));
        // Sin columnas auxiliares de retención a la derecha.
        Assert.True(ws.Cell(h, 11).IsEmpty());
        Assert.Equal(4571.75, Numero(ws.Cell(h + 2 + 4 + 1, "I")), 2);
    }

    [Fact]
    public async Task Cobrador_y_titulo_son_configurables_y_no_hay_nada_fijo_a_una_empresa()
    {
        var cfg = new ConfiguracionPwc { Cobrador = "ACME", EncabezadoRazonSocial = "EMISORA", Titulo = "CARTERA POR COBRAR" };
        var (wb, ws, h) = await Generar(cfg);
        using var _ = wb;

        Assert.Equal("EMISORA", ws.Cell(h, "A").GetString());
        Assert.Equal("MONTO A COBRAR POR ACME", ws.Cell(h, "O").GetString());
        Assert.Equal($"CARTERA POR COBRAR — {Empresa}", ws.Cell(1, 1).GetString());
        Assert.Equal(Empresa, ws.Cell(h + 2, "A").GetString());
        Assert.Contains("Corte: 21/09/2026", ws.Cell(2, 1).GetString());
        Assert.DoesNotContain(ws.CellsUsed(), c => c.GetString().Contains("EFEMEDIO", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("CXC ACME", ws.Name);
    }

    [Fact]
    public async Task Sin_facturas_genera_el_archivo_con_encabezado_y_totales_en_cero()
    {
        var (wb, ws, h) = await Generar(resultado: ResultadoPwc.Vacio);
        using var _ = wb;

        Assert.Equal("FACTURA", ws.Cell(h, "B").GetString());
        Assert.Equal("TOTALES", ws.Cell(h + 2 + 1, "E").GetString());
        Assert.Equal(0, Numero(ws.Cell(h + 2 + 1, "O")));
    }

    [Fact]
    public void Nombre_de_archivo_lleva_cobrador_empresa_y_fecha_sin_caracteres_invalidos()
    {
        var exp = new PwcExcelExporter();
        Assert.Equal("CXC PWC RADIO FM DEMO CIA. LTDA. 20260921.xlsx",
            exp.NombreArchivo(new ConfiguracionPwc(), Empresa, Corte));
        Assert.DoesNotContain('/', exp.NombreArchivo(new ConfiguracionPwc { Cobrador = "A/B" }, "X:Y", Corte));
    }
}
