using PsaWeb.Ventas.Prefacturas;
using UglyToad.PdfPig;

namespace PsaWeb.Ventas.Tests;

public class PrefacturaPdfCorreoTests
{
    private static Prefactura Ejemplo()
    {
        var s = PrefacturaLogicaTests.Solicitud(
            PrefacturaLogicaTests.Linea("RT18Z-32/2P EBAS", 2, 6.95m),
            PrefacturaLogicaTests.Linea("EBS2UZ2P1000VDC", 3, 46.51m, lista: 48.96m, existencia: 27m));
        var lineas = CalculadoraPrefactura.Calcular(s.Lineas);
        var (sub, iva, total) = CalculadoraPrefactura.Totales(lineas, true, 15m);
        return new Prefactura(7, "1791313747001", "SANCEV CIA. LTDA.", 12, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 17), EstadoPrefactura.Emitida,
            s.ClienteId, s.ClienteNombre, s.ClienteContacto, s.ClienteTelefono, s.ClienteEmail, 2, 30, "Net 30 Days", s.Vendedor, s.Etiqueta, s.OrdenCliente,
            s.DireccionEnvio, s.NotaCliente, s.NotaInterna, "4-15%", 15m, sub, iva, total, lineas, "vendedor1", new DateTime(2026, 10, 2, 15, 0, 0, DateTimeKind.Utc),
            EstadoCorreo.Pendiente, string.Empty, null, null, null, null, null);
    }

    private static string TextoDelPdf(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return string.Join("\n", doc.GetPages().Select(p => p.Text));
    }

    [Fact]
    public void El_PDF_del_cliente_trae_numero_vigencia_lineas_y_totales()
    {
        var texto = TextoDelPdf(PrefacturaPdf.Generar(Ejemplo()));
        Assert.Contains("PF-0012", texto);
        Assert.Contains("COTIZACIÓN", texto);
        Assert.Contains("SANCEV CIA. LTDA.", texto);
        Assert.Contains("RODRIGUEZ FERNANDEZ CARLOTA GABRIELA", texto);
        Assert.Contains("RT18Z-32/2P EBAS", texto);
        Assert.Contains("17/10/2026", texto);
        Assert.Contains("13,90", texto);
        Assert.Contains("139,53", texto);
        Assert.Contains("153,43", texto);
        Assert.Contains("23,01", texto);
        Assert.Contains("176,44", texto);
        Assert.Contains("Entrega en 3 días", texto);
    }

    [Fact]
    public void El_PDF_del_cliente_no_revela_datos_internos()
    {
        var texto = TextoDelPdf(PrefacturaPdf.Generar(Ejemplo()));
        Assert.DoesNotContain("Cliente exigente", texto);
        Assert.DoesNotContain("JACHO WILSON (EQU)", texto);
        Assert.DoesNotContain("48,96", texto); // precio de lista distinto del facturado
        Assert.Contains("Este documento no es una factura", texto);
    }

    [Fact]
    public void Una_cotizacion_larga_pagina_y_numera_las_hojas()
    {
        var lineas = Enumerable.Range(1, 80).Select(i => PrefacturaLogicaTests.Linea("ITEM-" + i, 1, 10m)).ToArray();
        var s = PrefacturaLogicaTests.Solicitud(lineas);
        var calculadas = CalculadoraPrefactura.Calcular(s.Lineas);
        var p = Ejemplo() with { Lineas = calculadas };
        using var doc = PdfDocument.Open(PrefacturaPdf.Generar(p));
        Assert.True(doc.NumberOfPages > 1);
        Assert.Contains("ITEM-80", string.Join(" ", doc.GetPages().Select(x => x.Text)));
    }

    [Fact]
    public void Generar_en_paralelo_no_corrompe_el_texto()
    {
        // QuestPDF 2026.8.0 cambiaba glifos al generar varios PDF a la vez (p. ej. "13,90" salía "qs,90"); corregido en 2026.9.x.
        var p = Ejemplo();
        var esperado = TextoDelPdf(PrefacturaPdf.Generar(p));
        var pdfs = new System.Collections.Concurrent.ConcurrentBag<byte[]>();
        Parallel.For(0, 200, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ => pdfs.Add(PrefacturaPdf.Generar(p)));
        Assert.All(pdfs, pdf => Assert.Equal(esperado, TextoDelPdf(pdf)));
    }

    [Fact]
    public void El_nombre_de_archivo_es_valido_en_Windows()
    {
        var p = Ejemplo() with { ClienteNombre = "A/B: \"C\" <D>?" };
        var nombre = PrefacturaPdf.NombreDeArchivo(p);
        Assert.StartsWith("PF-0012-", nombre);
        Assert.EndsWith(".pdf", nombre);
        Assert.Equal(-1, nombre.IndexOfAny(Path.GetInvalidFileNameChars()));
    }

    [Fact]
    public void El_correo_a_Contabilidad_lleva_todos_los_campos_para_digitar_en_Sage()
    {
        var p = Ejemplo();
        var html = PrefacturaCorreo.CuerpoHtml(p, "https://portal/ventas/prefacturas/7");
        foreach (var esperado in new[]
                 {
                     "RODRIGUEZ FERNANDEZ CARLOTA GABRIELA", "CARLOTA RODRIGUEZ", "WILSON JACHO", "JACHO WILSON (EQU)", "Net 30 Days", "4-15%",
                     "OC-77", "Av. Amazonas y Colón, Quito", "Cliente exigente: confirmar stock", "RT18Z-32/2P EBAS", "EBS2UZ2P1000VDC",
                     "139,53", "153,43", "23,01", "176,44", "48,96", "46,51", "https://portal/ventas/prefacturas/7",
                 })
        {
            Assert.Contains(System.Net.WebUtility.HtmlEncode(esperado), html);
        }
        Assert.Contains("✎", html); // la segunda línea tiene precio manual (46,51 vs lista 48,96)
        Assert.Contains("válida hasta el 17/10/2026", html);
        Assert.Equal("[Prefactura PF-0012] RODRIGUEZ FERNANDEZ CARLOTA GABRIELA — total 176,44 — WILSON JACHO", PrefacturaCorreo.Asunto(p));
    }

    [Fact]
    public void El_correo_escapa_el_HTML_que_digite_el_vendedor()
    {
        var p = Ejemplo() with { NotaInterna = "<script>alert(1)</script>", ClienteNombre = "A & B <C>" };
        var html = PrefacturaCorreo.CuerpoHtml(p);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("A &amp; B &lt;C&gt;", html);
    }
}
