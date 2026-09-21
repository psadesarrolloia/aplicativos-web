using SixLabors.Fonts;

namespace PsaWeb.Modules.Reportes.Cheques;

/// <summary>Mide el ancho de un texto (en mm) para una fuente y un tamaño (en puntos).</summary>
public interface IMedidorTexto
{
    double AnchoMm(string texto, double tamanoPt);
}

/// <summary>Fuente monoespaciada (Courier, Consolas…): cada carácter mide <c>0,6 × tamaño</c>.</summary>
public sealed class MedidorMonoespaciado : IMedidorTexto
{
    public const double AnchoEm = 0.6;

    public double AnchoMm(string texto, double tamanoPt) => texto.Length * Medidas.PuntosAMm(tamanoPt * AnchoEm);
}

/// <summary>
/// Mide con las métricas reales de una fuente instalada (SixLabors.Fonts, la misma librería de ClosedXML). Sirve para
/// fuentes proporcionales como Arial, la del reporte original de Access: sin esto habría que suponer el ancho de cada letra.
/// </summary>
public sealed class MedidorFuente(FontFamily familia) : IMedidorTexto
{
    public double AnchoMm(string texto, double tamanoPt)
    {
        if (texto.Length == 0)
        {
            return 0;
        }
        var font = familia.CreateFont((float)tamanoPt);
        // Con 72 ppp, 1 unidad = 1 punto.
        var opciones = new TextOptions(font) { Dpi = 72 };
        return Medidas.PuntosAMm(TextMeasurer.MeasureAdvance(texto, opciones).Width);
    }
}

public static class MedidorTexto
{
    private static readonly string[] Monoespaciadas =
        { "Courier New", "Courier", "Consolas", "Lucida Console", "Lucida Sans Typewriter", "Liberation Mono", "DejaVu Sans Mono" };

    /// <summary>
    /// Medidor para <paramref name="fuente"/>: exacto si está instalada y es proporcional; monoespaciado si es de ancho fijo o
    /// si no se puede medir (estimación conservadora: 0,6 em por carácter).
    /// </summary>
    public static IMedidorTexto Para(string? fuente)
    {
        if (string.IsNullOrWhiteSpace(fuente) || Monoespaciadas.Contains(fuente.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return new MedidorMonoespaciado();
        }
        try
        {
            if (SystemFonts.TryGet(fuente.Trim(), out var familia))
            {
                return new MedidorFuente(familia);
            }
        }
        catch (Exception)
        {
            // sin acceso a las fuentes del sistema: cae a la estimación
        }
        return new MedidorMonoespaciado();
    }
}
