using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using PsaWeb.Modules.Reportes.Comun;

namespace PsaWeb.Modules.Reportes.Cheques;

/// <summary>
/// Renderiza <see cref="PaginaCheque"/> a PDF A4 vertical con QuestPDF (licencia Community, la misma del Talón del
/// ATS). Cada campo, línea y recuadro se coloca por su (x, y) absoluto en milímetros, con margen 0: la fuente sólo
/// cambia el ancho de cada texto, no su origen — por eso no se «corre» en la matricial como un PDF de flujo
/// (opción A del plan §5.4). Imprimir siempre al 100 % / «tamaño real», sin «ajustar a página».
/// </summary>
public sealed class ChequePdfRenderer
{
    public const string ContentType = "application/pdf";

    public byte[] Generar(IEnumerable<PaginaCheque> paginas, ConfiguracionCheque config, LogoEmpresa? logo, string titulo = "Cheque")
        => Crear(paginas, config, logo, titulo).GeneratePdf();

    /// <summary>Vista previa: una imagen PNG por hoja (para mostrar en pantalla antes de imprimir).</summary>
    public IReadOnlyList<byte[]> GenerarImagenes(IEnumerable<PaginaCheque> paginas, ConfiguracionCheque config, LogoEmpresa? logo, int dpi = 110)
        => Crear(paginas, config, logo, "Vista previa")
            .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = dpi })
            .ToList();

    private static Document Crear(IEnumerable<PaginaCheque> paginas, ConfiguracionCheque config, LogoEmpresa? logo, string titulo)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var fuente = string.IsNullOrWhiteSpace(config.Fuente) ? "Courier New" : config.Fuente;
        var lista = paginas.ToList();

        return Document.Create(doc =>
            {
                foreach (var pagina in lista)
                {
                    doc.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(0);
                        page.DefaultTextStyle(t => t.FontFamily(fuente).FontSize((float)config.Tamano).FontColor(Colors.Black));
                        page.Content().Layers(layers =>
                        {
                            layers.PrimaryLayer().ExtendHorizontal().ExtendVertical();
                            Dibujar(layers, pagina, fuente, logo);
                        });
                    });
                }
            })
            .WithMetadata(new DocumentMetadata { Title = titulo, Author = "Aplicativos web PSA" });
    }

    private static void Dibujar(LayersDescriptor layers, PaginaCheque pagina, string fuente, LogoEmpresa? logo)
    {
        foreach (var r in pagina.Rectangulos)
        {
            layers.Layer()
                .OffsetX((float)r.X, Unit.Millimetre).OffsetY((float)r.Y, Unit.Millimetre)
                .Width((float)r.Ancho, Unit.Millimetre).Height((float)r.Alto, Unit.Millimetre)
                .Border((float)r.Grosor, Unit.Millimetre).BorderColor(Colors.Black);
        }

        foreach (var l in pagina.Lineas)
        {
            // Sólo se usan líneas horizontales (encabezado de tabla, pie de totales, firmas, reglas).
            var largo = Math.Abs(l.X2 - l.X1);
            if (l.Y1 == l.Y2 && largo > 0)
            {
                layers.Layer()
                    .OffsetX((float)Math.Min(l.X1, l.X2), Unit.Millimetre).OffsetY((float)l.Y1, Unit.Millimetre)
                    .Width((float)largo, Unit.Millimetre)
                    .LineHorizontal((float)l.Grosor, Unit.Millimetre).LineColor(Colors.Black);
            }
            else if (l.X1 == l.X2 && Math.Abs(l.Y2 - l.Y1) > 0)
            {
                layers.Layer()
                    .OffsetX((float)l.X1, Unit.Millimetre).OffsetY((float)Math.Min(l.Y1, l.Y2), Unit.Millimetre)
                    .Height((float)Math.Abs(l.Y2 - l.Y1), Unit.Millimetre)
                    .LineVertical((float)l.Grosor, Unit.Millimetre).LineColor(Colors.Black);
            }
        }

        if (pagina.Logo is { } pos && logo is { Contenido.Length: > 0 })
        {
            layers.Layer()
                .OffsetX((float)pos.X, Unit.Millimetre).OffsetY((float)pos.Y, Unit.Millimetre)
                .Width((float)pos.Ancho, Unit.Millimetre).Height((float)pos.Alto, Unit.Millimetre)
                .Image(logo.Contenido).FitArea();
        }

        foreach (var c in pagina.Campos)
        {
            if (c.Texto.Length == 0)
            {
                continue;
            }

            var caja = layers.Layer()
                .OffsetX((float)c.X, Unit.Millimetre).OffsetY((float)c.Y, Unit.Millimetre)
                .Width((float)c.Ancho, Unit.Millimetre);

            caja.Text(t =>
            {
                switch (c.Alineacion)
                {
                    case Alineacion.Derecha: t.AlignRight(); break;
                    case Alineacion.Centro: t.AlignCenter(); break;
                    default: t.AlignLeft(); break;
                }
                t.ClampLines(1, "");
                var span = t.Span(c.Texto).FontSize((float)c.Tamano).FontFamily(fuente);
                if (c.Negrita)
                {
                    span.Bold();
                }
            });
        }
    }
}
