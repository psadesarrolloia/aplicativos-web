using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PsaWeb.Ventas.Prefacturas;

/// <summary>Formatos de número y fecha del portal (es-EC: miles con punto, decimales con coma).</summary>
public static class FormatoVentas
{
    public static readonly CultureInfo Ec = CultureInfo.GetCultureInfo("es-EC");

    public static string Dinero(decimal v) => v.ToString("N2", Ec);

    public static string Cantidad(decimal v) => v.ToString("#,##0.####", Ec);

    public static string Fecha(DateOnly f) => f.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}

/// <summary>
/// PDF de la cotización/prefactura que el vendedor descarga en su dispositivo y entrega al cliente (QuestPDF, licencia Community, igual que el Talón del ATS).
/// Es el documento <b>para el cliente</b>: no lleva notas internas, existencias ni la etiqueta de facturación; esos datos van solo en el correo a Contabilidad.
/// </summary>
public static class PrefacturaPdf
{
    static PrefacturaPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static string NombreDeArchivo(Prefactura p) => $"{p.NumeroTexto}-{Limpiar(p.ClienteNombre)}.pdf";

    public static byte[] Generar(Prefactura p)
    {
        var documento = Document.Create(contenedor =>
        {
            contenedor.Page(pagina =>
            {
                pagina.Size(PageSizes.A4);
                pagina.Margin(32);
                pagina.DefaultTextStyle(x => x.FontSize(9));

                pagina.Header().Element(c => Cabecera(c, p));
                pagina.Content().Element(c => Cuerpo(c, p));
                pagina.Footer().Column(col =>
                {
                    col.Item().BorderTop(0.5f).PaddingTop(4).Text(
                        $"Cotización válida hasta el {FormatoVentas.Fecha(p.ValidaHasta)}. Precios y existencias sujetos a cambio sin previo aviso. " +
                        "Este documento no es una factura.").FontSize(7.5f).FontColor(Colors.Grey.Darken2);
                    col.Item().AlignRight().Text(t =>
                    {
                        t.Span($"{p.NumeroTexto} — Página ").FontSize(7.5f);
                        t.CurrentPageNumber().FontSize(7.5f);
                        t.Span(" de ").FontSize(7.5f);
                        t.TotalPages().FontSize(7.5f);
                    });
                });
            });
        });

        return documento.GeneratePdf();
    }

    private static void Cabecera(IContainer contenedor, Prefactura p)
    {
        contenedor.PaddingBottom(8).Row(fila =>
        {
            fila.RelativeItem().Column(c =>
            {
                c.Item().Text(p.EmpresaNombre).Bold().FontSize(13);
                c.Item().Text($"RUC {p.Ruc}").FontSize(9).FontColor(Colors.Grey.Darken2);
            });
            fila.ConstantItem(190).Column(c =>
            {
                c.Item().AlignRight().Text("COTIZACIÓN").Bold().FontSize(15).FontColor("#163154");
                c.Item().AlignRight().Text($"N.º {p.NumeroTexto}").Bold().FontSize(11);
                c.Item().AlignRight().Text($"Fecha: {FormatoVentas.Fecha(p.FechaEmision)}");
                c.Item().AlignRight().Text($"Válida hasta: {FormatoVentas.Fecha(p.ValidaHasta)}").FontColor("#B40046").SemiBold();
            });
        });
    }

    private static void Cuerpo(IContainer contenedor, Prefactura p)
    {
        contenedor.Column(col =>
        {
            col.Spacing(10);

            col.Item().Background(Colors.Grey.Lighten4).Padding(8).Row(fila =>
            {
                fila.RelativeItem().Column(c =>
                {
                    c.Item().Text("CLIENTE").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                    c.Item().Text(p.ClienteNombre).Bold().FontSize(10);
                    c.Item().Text($"Código: {p.ClienteId}");
                    if (p.ClienteContacto.Length > 0) c.Item().Text($"Contacto: {p.ClienteContacto}");
                    if (p.ClienteTelefono.Length > 0) c.Item().Text($"Teléfono: {p.ClienteTelefono}");
                    if (p.ClienteEmail.Length > 0) c.Item().Text($"Correo: {p.ClienteEmail}");
                    if (p.DireccionEnvio.Length > 0) c.Item().Text($"Entrega en: {p.DireccionEnvio}");
                });
                fila.ConstantItem(190).Column(c =>
                {
                    c.Item().Text("CONDICIONES").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                    c.Item().Text($"Asesor comercial: {p.Vendedor}");
                    if (p.Terminos.Length > 0) c.Item().Text($"Crédito: {p.Terminos.Replace("Net", "Neto").Replace("Days", "días").Replace("Day", "día")}");
                    if (p.OrdenCliente.Length > 0) c.Item().Text($"Su orden: {p.OrdenCliente}");
                });
            });

            col.Item().Table(tabla =>
            {
                tabla.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(22);
                    c.ConstantColumn(78);
                    c.RelativeColumn();
                    c.ConstantColumn(50);
                    c.ConstantColumn(58);
                    c.ConstantColumn(64);
                });

                tabla.Header(h =>
                {
                    Encabezado(h.Cell(), "#", false);
                    Encabezado(h.Cell(), "Código", false);
                    Encabezado(h.Cell(), "Descripción", false);
                    Encabezado(h.Cell(), "Cant.", true);
                    Encabezado(h.Cell(), "P. unit.", true);
                    Encabezado(h.Cell(), "Subtotal", true);
                });

                foreach (var l in p.Lineas)
                {
                    Celda(tabla.Cell(), l.Orden.ToString(CultureInfo.InvariantCulture), false);
                    Celda(tabla.Cell(), l.ItemId, false);
                    Celda(tabla.Cell(), l.Descripcion, false);
                    Celda(tabla.Cell(), FormatoVentas.Cantidad(l.Cantidad) + (l.UnidadMedida.Length > 0 ? " " + l.UnidadMedida : string.Empty), true);
                    Celda(tabla.Cell(), FormatoVentas.Dinero(l.PrecioUnitario), true);
                    Celda(tabla.Cell(), FormatoVentas.Dinero(l.Monto), true);
                }
            });

            col.Item().AlignRight().Width(200).Column(c =>
            {
                Total(c, "Subtotal", p.Subtotal, false);
                if (p.PorcentajeIva > 0 && p.Iva > 0) Total(c, $"IVA {p.PorcentajeIva.ToString("0.##", FormatoVentas.Ec)} %", p.Iva, false);
                Total(c, "TOTAL", p.Total, true);
            });

            if (p.NotaCliente.Length > 0)
            {
                col.Item().Column(c =>
                {
                    c.Item().Text("Observaciones").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                    c.Item().Text(p.NotaCliente);
                });
            }
        });
    }

    private static void Encabezado(IContainer c, string texto, bool derecha)
    {
        var x = c.Background("#163154").PaddingVertical(4).PaddingHorizontal(4);
        if (derecha) x = x.AlignRight();
        x.Text(texto).Bold().FontColor(Colors.White).FontSize(8);
    }

    private static void Celda(IContainer c, string texto, bool derecha)
    {
        var x = c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).PaddingHorizontal(4);
        if (derecha) x = x.AlignRight();
        x.Text(texto);
    }

    private static void Total(ColumnDescriptor c, string etiqueta, decimal valor, bool destacado)
    {
        c.Item().PaddingVertical(1.5f).Row(f =>
        {
            f.RelativeItem().Text(etiqueta).Bold();
            f.ConstantItem(80).AlignRight().Text(FormatoVentas.Dinero(valor)).Bold().FontSize(destacado ? 11 : 9);
        });
    }

    private static string Limpiar(string texto)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var limpio = new string(texto.Where(ch => !invalidos.Contains(ch)).ToArray()).Trim();
        return limpio.Length > 40 ? limpio[..40].Trim() : limpio;
    }
}
