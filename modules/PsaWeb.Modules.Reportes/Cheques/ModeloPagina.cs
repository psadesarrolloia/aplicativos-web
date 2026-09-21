namespace PsaWeb.Modules.Reportes.Cheques;

public enum Alineacion
{
    Izquierda,
    Derecha,
    Centro,
}

/// <summary>Un texto de una sola línea colocado en (X, Y) mm desde la esquina superior izquierda de la hoja.</summary>
public sealed record CampoPagina(
    string Id,
    double X,
    double Y,
    double Ancho,
    string Texto,
    Alineacion Alineacion = Alineacion.Izquierda,
    double Tamano = 10,
    bool Negrita = false);

public sealed record LineaPagina(double X1, double Y1, double X2, double Y2, double Grosor = 0.25);

public sealed record RectanguloPagina(double X, double Y, double Ancho, double Alto, double Grosor = 0.25);

public sealed record ImagenPagina(double X, double Y, double Ancho, double Alto);

/// <summary>
/// Una hoja A4 vertical descrita como listas de campos, líneas y recuadros con coordenadas absolutas en milímetros
/// (origen = esquina superior izquierda de la hoja). Es el ÚNICO modelo de la página: hoy lo consume el
/// renderizador PDF; un renderizador ESC/P (matricial en modo texto) sería otro consumidor de la misma lista, y las
/// pruebas verifican las posiciones sobre este modelo.
/// </summary>
public sealed record PaginaCheque(
    IReadOnlyList<CampoPagina> Campos,
    IReadOnlyList<LineaPagina> Lineas,
    IReadOnlyList<RectanguloPagina> Rectangulos,
    ImagenPagina? Logo = null)
{
    public const double AnchoMm = 210.0;
    public const double AltoMm = 297.0;
}

/// <summary>Medidas de la hoja A4 en puntos (1 pt = 1/72″) y conversión desde milímetros.</summary>
public static class Medidas
{
    public const double MmPorPunto = 25.4 / 72.0;

    public static double PuntosAMm(double puntos) => puntos * MmPorPunto;

    public static double MmAPuntos(double mm) => mm / MmPorPunto;

    /// <summary>Twips (1/1440″) → mm. Las medidas del .mdb están en twips.</summary>
    public static double TwipsAMm(double twips) => twips * 25.4 / 1440.0;
}

/// <summary>
/// Ajuste de texto para fuente monoespaciada: cada carácter mide <c>0,6 × tamaño</c>. Con esa hipótesis el texto se
/// parte y se achica de forma determinista, sin depender del motor de PDF, para que nunca se salga de su caja
/// (el reporte de Access dependía de que Arial 10 cupiera: 90 caracteres en 2 líneas de 100 mm).
/// </summary>
public static class AjusteTexto
{
    /// <summary>Ancho de un carácter de una fuente monoespaciada (Courier), en em.</summary>
    public const double AnchoEmMonoespaciada = 0.6;

    public const double TamanoMinimo = 5.0;

    public static double AnchoCaracterMm(double tamanoPt) => Medidas.PuntosAMm(tamanoPt * AnchoEmMonoespaciada);

    public static int CaracteresPorLinea(double anchoMm, double tamanoPt)
        => Math.Max(1, (int)Math.Floor((anchoMm + 1e-6) / AnchoCaracterMm(tamanoPt)));

    /// <summary>
    /// Tamaño (≤ <paramref name="tamanoPt"/>) con el que <paramref name="texto"/> cabe en UNA línea de
    /// <paramref name="anchoMm"/>.
    /// </summary>
    public static double TamanoParaUnaLinea(string texto, double anchoMm, double tamanoPt)
    {
        if (texto.Length == 0 || CaracteresPorLinea(anchoMm, tamanoPt) >= texto.Length)
        {
            return tamanoPt;
        }
        var t = tamanoPt;
        while (t > TamanoMinimo && CaracteresPorLinea(anchoMm, t) < texto.Length)
        {
            t -= 0.25;
        }
        return Math.Max(TamanoMinimo, t);
    }

    /// <summary>
    /// Parte <paramref name="texto"/> en a lo sumo <paramref name="maxLineas"/> líneas de <paramref name="anchoMm"/>,
    /// cortando en espacios (o en seco si una palabra no cabe) y achicando la fuente hasta que quepa.
    /// </summary>
    public static (IReadOnlyList<string> Lineas, double Tamano) PartirEnLineas(
        string texto, double anchoMm, double tamanoPt, int maxLineas)
    {
        var t = tamanoPt;
        while (true)
        {
            var lineas = Partir(texto, CaracteresPorLinea(anchoMm, t));
            if (lineas.Count <= maxLineas || t <= TamanoMinimo)
            {
                return (lineas.Take(maxLineas).ToList(), t);
            }
            t = Math.Max(TamanoMinimo, t - 0.25);
        }
    }

    private static List<string> Partir(string texto, int porLinea)
    {
        var lineas = new List<string>();
        var actual = "";
        foreach (var palabra in texto.Split(' ', StringSplitOptions.None))
        {
            var p = palabra;
            // Palabra más larga que la línea: se corta en seco.
            while (p.Length > porLinea)
            {
                if (actual.Length > 0)
                {
                    lineas.Add(actual);
                    actual = "";
                }
                lineas.Add(p[..porLinea]);
                p = p[porLinea..];
            }

            if (actual.Length == 0)
            {
                actual = p;
            }
            else if (actual.Length + 1 + p.Length <= porLinea)
            {
                actual += " " + p;
            }
            else
            {
                lineas.Add(actual);
                actual = p;
            }
        }
        if (actual.Length > 0 || lineas.Count == 0)
        {
            lineas.Add(actual);
        }
        return lineas;
    }
}
