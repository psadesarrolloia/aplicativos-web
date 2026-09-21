using PsaWeb.Modules.Reportes.Cheques;
using PsaWeb.Modules.Reportes.Comun;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PsaWeb.Reportes.Tests;

/// <summary>
/// Verifica el PDF real (no sólo el modelo): tamaño A4 y posición de cada texto en milímetros desde la esquina
/// superior izquierda de la hoja, leída con PdfPig del propio PDF generado.
/// </summary>
public class ChequePdfTests
{
    // 1×1 PNG transparente.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static PagoConDetalle Pago(string benef = "TONY VERA", decimal monto = 35m) => new(
        new PagoCheque(1, new DateOnly(2026, 9, 18), benef, ReferenciaPago.Analizar("3094")!, monto),
        new List<LineaPago>
        {
            new(0, "10302-311", "Banco", "TONY VERA", -monto, "", ""),
            new(1, "20022-521", "Cuenta", "SERVICIOS", monto, "FAC-001", "PROV"),
        });

    /// <summary>Origen en la esquina de la hoja (0, 0) para verificar las constantes crudas; el resto, valores por defecto (Arial, punto decimal).</summary>
    private static ConfiguracionCheque Base(double x = 0, double y = 0) => new() { CorreccionX = x, CorreccionY = y };

    private static byte[] Pdf(ConfiguracionCheque? cfg = null, LogoEmpresa? logo = null, OpcionesImpresion? o = null, PagoConDetalle? pago = null)
    {
        cfg ??= Base();
        var paginas = ConstructorPaginaCheque.Construir(pago ?? Pago(), cfg, "EMPRESA DEMO S.A.", "Guayaquil", logo is not null, o ?? new OpcionesImpresion());
        return new ChequePdfRenderer().Generar(paginas, cfg, logo);
    }

    /// <summary>Esquina superior izquierda de la palabra, en mm desde la esquina superior izquierda de la hoja.</summary>
    private static (double X, double Y) Posicion(Page page, string palabra)
    {
        var w = page.GetWords().First(x => x.Text == palabra);
        return (Medidas.PuntosAMm(w.BoundingBox.Left), Medidas.PuntosAMm(page.Height - w.BoundingBox.Top));
    }

    [Fact]
    public void El_PDF_es_una_hoja_A4_vertical_por_pago()
    {
        using var doc = PdfDocument.Open(Pdf());
        Assert.Equal(1, doc.NumberOfPages);
        var page = doc.GetPage(1);
        Assert.Equal(210.0, Medidas.PuntosAMm(page.Width), 0);
        Assert.Equal(297.0, Medidas.PuntosAMm(page.Height), 0);
    }

    [Fact]
    public void Varios_pagos_son_varias_hojas()
    {
        var cfg = Base();
        var paginas = new[] { Pago(), Pago("OTRO BENEFICIARIO") }
            .SelectMany(p => ConstructorPaginaCheque.Construir(p, cfg, "EMPRESA", "Quito", false, new OpcionesImpresion()))
            .ToList();
        using var doc = PdfDocument.Open(new ChequePdfRenderer().Generar(paginas, cfg, null));
        Assert.Equal(2, doc.NumberOfPages);
    }

    [Fact]
    public void Cada_campo_del_cheque_cae_en_su_posicion_en_mm()
    {
        using var doc = PdfDocument.Open(Pdf(o: new OpcionesImpresion(Comprobante: false)));
        var page = doc.GetPage(1);

        // X: el texto arranca en la x del modelo (± 0,6 mm de holgura por el márgen lateral del glifo).
        // Y: la parte alta del glifo cae entre la y del modelo y unos ~3 mm más abajo (interlineado del motor de texto).
        void Verifica(string palabra, double x, double y)
        {
            var (px, py) = Posicion(page, palabra);
            Assert.InRange(px, x - 0.6, x + 0.9);
            Assert.InRange(py, y - 0.5, y + 3.0);
        }

        Verifica("TONY", 15.0, 7.0);        // beneficiario
        Verifica("3094", 170.1, 5.0);       // n.º de cheque
        Verifica("CH.No.:", 170.1, 0.0);    // rótulo
        Verifica("TREINTA", 15.1, 15.0);    // monto en letras (35 → «TREINTA Y CINCO …»)
    }

    [Fact]
    public void El_monto_va_alineado_a_la_derecha_de_su_caja_de_25_mm()
    {
        using var doc = PdfDocument.Open(Pdf(o: new OpcionesImpresion(Comprobante: false)));
        var page = doc.GetPage(1);

        var w = page.GetWords().First(x => x.Text == "35.00");
        var derecha = Medidas.PuntosAMm(w.BoundingBox.Right);
        Assert.InRange(derecha, 109.0 + 25.0 - 0.8, 109.0 + 25.0 + 0.3); // borde derecho de la caja (109 + 25 = 134)
    }

    [Fact]
    public void La_correccion_XY_mueve_el_texto_esos_milimetros_en_el_PDF()
    {
        var sin = PdfDocument.Open(Pdf(o: new OpcionesImpresion(Comprobante: false)));
        var con = PdfDocument.Open(Pdf(Base(4.0, 2.0), o: new OpcionesImpresion(Comprobante: false)));
        using (sin)
        using (con)
        {
            var (x0, y0) = Posicion(sin.GetPage(1), "TONY");
            var (x1, y1) = Posicion(con.GetPage(1), "TONY");
            Assert.Equal(4.0, x1 - x0, 0.1);
            Assert.Equal(2.0, y1 - y0, 0.1);
        }
    }

    [Fact]
    public void El_comprobante_esta_debajo_del_cheque_y_las_firmas_en_el_pie()
    {
        // Con el desplazamiento por defecto (margen de Access 10 Â· 13): tÃ­tulo en y = 86,7 + 13; firmas en y = 253,7.
        using var doc = PdfDocument.Open(Pdf(new ConfiguracionCheque()));
        var page = doc.GetPage(1);

        Assert.Equal("COMPROBANTE", page.GetWords().First(w => w.Text == "COMPROBANTE").Text);
        var (_, yTitulo) = Posicion(page, "COMPROBANTE");
        Assert.InRange(yTitulo, 99.7 - 0.5, 99.7 + 3.0);

        var (xFirma, yFirma) = Posicion(page, "RECIBIDO");
        Assert.InRange(yFirma, 253.7, 253.7 + 3.5);
        Assert.InRange(xFirma, 33.0, 33.0 + 40.0); // centrado en su línea de 40 mm
        Assert.Contains(page.GetWords(), w => w.Text == "CODIGO");
        Assert.Contains(page.GetWords(), w => w.Text == "EMPRESA");
    }

    [Fact]
    public void Se_usa_Arial_por_defecto_y_Courier_si_se_configura()
    {
        using var arial = PdfDocument.Open(Pdf());
        Assert.Contains(arial.GetPage(1).Letters.Select(l => l.FontName ?? "").Distinct(), f => f.Contains("Arial", StringComparison.OrdinalIgnoreCase));

        using var courier = PdfDocument.Open(Pdf(new ConfiguracionCheque { Fuente = "Courier New", CorreccionX = 0, CorreccionY = 0 }));
        Assert.Contains(courier.GetPage(1).Letters.Select(l => l.FontName ?? "").Distinct(), f => f.Contains("Cour", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void El_logo_se_incrusta_solo_si_hay()
    {
        using var sin = PdfDocument.Open(Pdf());
        Assert.Empty(sin.GetPage(1).GetImages());

        using var con = PdfDocument.Open(Pdf(logo: new LogoEmpresa(Png, "image/png")));
        Assert.Single(con.GetPage(1).GetImages());
    }

    [Fact]
    public void El_texto_en_letras_de_2_lineas_queda_dentro_de_los_100_mm()
    {
        using var doc = PdfDocument.Open(Pdf(pago: Pago(monto: 1234.56m), o: new OpcionesImpresion(Comprobante: false)));
        var page = doc.GetPage(1);
        var palabras = page.GetWords().Where(w => Medidas.PuntosAMm(page.Height - w.BoundingBox.Top) is > 14.0 and < 25.0).ToList();

        Assert.NotEmpty(palabras);
        Assert.All(palabras, w => Assert.InRange(Medidas.PuntosAMm(w.BoundingBox.Right), 15.0, 15.1 + 100.0 + 0.5));
    }

    [Fact]
    public async Task La_hoja_de_prueba_es_A4_trae_la_regla_de_100_mm_y_el_cheque_de_ejemplo()
    {
        var servicio = new ServicioImpresionCheques(new SampleChequesRepository(), new ServicioConfiguracionMemoria(), new ChequePdfRenderer());
        var pdf = await servicio.GenerarHojaPruebaAsync(null, "EMPRESA DEMO");

        using var doc = PdfDocument.Open(pdf);
        Assert.Equal(1, doc.NumberOfPages);
        var texto = string.Join(" ", doc.GetPage(1).GetWords().Select(w => w.Text));
        Assert.Contains("PRUEBA", texto);
        Assert.Contains("100,0", texto);
        Assert.Contains("BENEFICIARIO", texto);
        Assert.Contains("1,234.56", texto);
    }

    [Fact]
    public void La_hoja_de_prueba_marca_la_esquina_la_regla_de_100_mm_y_el_margen_de_Access()
    {
        var p = HojaPruebaCheque.Construir(Base(), "EMPRESA", "Quito");

        Assert.Contains(p.Lineas, l => l is { X1: 10, X2: 110, Y1: 46, Y2: 46 });                       // 100,0 mm exactos
        Assert.Contains(p.Lineas, l => l.X1 == HojaPruebaCheque.MargenAccessX - 4.0 && l.Y1 == HojaPruebaCheque.MargenAccessY); // cruz en (10; 13)
        Assert.Contains(p.Campos, c => c.Texto.Contains("margen del reporte de Access"));
        Assert.True(p.Rectangulos.Count >= 5); // un recuadro por campo del cheque de ejemplo

        // La corrección vigente se refleja en el cheque de ejemplo pero NO en las reglas (referencias de la hoja).
        var conCorreccion = HojaPruebaCheque.Construir(Base(2, 3), "EMPRESA", "Quito");
        Assert.Contains(conCorreccion.Lineas, l => l is { X1: 10, X2: 110, Y1: 46, Y2: 46 });
        Assert.Equal(p.Campos.Single(c => c.Id == "beneficiario").X + 2, conCorreccion.Campos.Single(c => c.Id == "beneficiario").X);
    }

    // --- servicio de impresión -------------------------------------------------------------------------------------

    private static ServicioImpresionCheques Servicio(ServicioConfiguracionMemoria? cfg = null)
        => new(new SampleChequesRepository(), cfg ?? new ServicioConfiguracionMemoria(), new ChequePdfRenderer());

    [Fact]
    public async Task El_servicio_genera_el_pdf_de_los_pagos_pedidos_en_el_orden_pedido()
    {
        var pdf = await Servicio().GenerarPdfAsync(null, "EMPRESA DEMO S.A.", new long[] { 5002, 5003 }, new OpcionesImpresion());

        Assert.NotNull(pdf);
        using var doc = PdfDocument.Open(pdf!);
        Assert.Equal(2, doc.NumberOfPages);
        Assert.Contains(doc.GetPage(1).GetWords(), w => w.Text == "VERONICA");
        Assert.Contains(doc.GetPage(2).GetWords(), w => w.Text == "TONY");
    }

    [Fact]
    public async Task El_servicio_usa_la_configuracion_de_la_empresa()
    {
        var cfg = new ServicioConfiguracionMemoria();
        await cfg.GuardarAsync("1790000000001", ClavesReporte.Empresa, new ConfiguracionEmpresa { NombreEmpresa = "MI EMISORA CIA. LTDA.", Ciudad = "Loja" }, "test");
        await cfg.GuardarAsync("1790000000001", ClavesReporte.Cheques, new ConfiguracionCheque { Firma2 = "AUTORIZADO" }, "test");
        await cfg.GuardarLogoAsync("1790000000001", new LogoEmpresa(Png, "image/png"), "test");

        var pdf = await Servicio(cfg).GenerarPdfAsync("1790000000001", "NOMBRE DEL SHELL", new long[] { 5003 }, new OpcionesImpresion());
        using var doc = PdfDocument.Open(pdf!);
        var page = doc.GetPage(1);
        var texto = string.Join(" ", page.GetWords().Select(w => w.Text));

        Assert.Contains("MI EMISORA", texto);
        Assert.DoesNotContain("SHELL", texto);
        Assert.Contains("Loja,", texto);
        Assert.Contains("AUTORIZADO", texto);
        Assert.Single(page.GetImages());
    }

    [Fact]
    public async Task Sin_configuracion_se_usa_el_nombre_del_shell_y_no_hay_ciudad_inventada()
    {
        var pdf = await Servicio().GenerarPdfAsync(null, "NOMBRE DEL SHELL", new long[] { 5003 }, new OpcionesImpresion());
        using var doc = PdfDocument.Open(pdf!);
        var texto = string.Join(" ", doc.GetPage(1).GetWords().Select(w => w.Text));

        Assert.Contains("SHELL", texto);
        Assert.DoesNotContain("Quito", texto);
    }

    [Fact]
    public async Task El_servicio_valida_los_pedidos()
    {
        var s = Servicio();
        Assert.Null(await s.GenerarPdfAsync(null, "E", new long[] { 999999 }, new OpcionesImpresion()));
        await Assert.ThrowsAsync<ArgumentException>(() => s.GenerarPdfAsync(null, "E", new long[] { 5003 }, new OpcionesImpresion(false, false)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            s.GenerarPdfAsync(null, "E", Enumerable.Range(1, ServicioImpresionCheques.MaximoPagos + 1).Select(i => (long)i).ToList(), new OpcionesImpresion()));
    }
}
