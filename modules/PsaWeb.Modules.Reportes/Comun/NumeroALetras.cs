using System.Text;

namespace PsaWeb.Modules.Reportes.Comun;

/// <summary>
/// Monto en letras del cheque. Port fiel de la <c>valor_letras</c> del reporte
/// <c>PaymentsProof</c> de Access (no de la del módulo <c>Numero a letras</c>, que no agrega el
/// «CON xx/100» ni el relleno): las funciones auxiliares <c>tunidades</c>, <c>tcientos</c>,
/// <c>tmiles</c> y <c>tmillones</c> del módulo se conservan tal cual, con sus rarezas.
/// <list type="bullet">
///   <item><b>Q2</b>: los bloques se concatenan con los espacios que trae cada función, así que
///   hay dobles espacios («UN MIL DOSCIENTOS  TREINTA Y CUATRO») y «UN MIL» en vez de «MIL».</item>
///   <item><b>Q1</b>: el «CON xx/100» y el relleno sólo se agregan si
///   <c>millones &gt; 0 Or miles &gt; 0 Or cientos &gt; 0 Or unidades &gt; 1</c>, por lo que los montos
///   menores a $2,00 (salvo $0,00 = «CERO DOLARES») salen sin centavos. Con
///   <c>corregirMenoresA2</c> el bloque de centavos se agrega siempre que el monto sea mayor que cero.</item>
/// </list>
/// El relleno completa con «x» hasta <see cref="TamanoPorDefecto"/> caracteres (<c>TamNumLetters</c>),
/// insertando un espacio cada 5 «x»; el límite del ciclo se calcula una sola vez, por eso el resultado
/// final puede pasar de 90 (los espacios no cuentan).
/// </summary>
public static class NumeroALetras
{
    /// <summary><c>TamNumLetters</c> del reporte de Access.</summary>
    public const int TamanoPorDefecto = 90;

    private static readonly string[] Unidad =
    {
        "UN", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE",
        " DIEZ", " ONCE", " DOCE", " TRECE", " CATORCE", " QUINCE",
        " DIECISEIS", " DIECISIETE", " DIECIOCHO", " DIECINUEVE",
    };

    private static readonly string[] Decena =
    {
        " TREINTA", " CUARENTA", " CINCUENTA", " SESENTA", " SETENTA", " OCHENTA", " NOVENTA",
    };

    private static readonly string[] Ciento =
    {
        " CIENTO", " DOSCIENTOS", " TRESCIENTOS", " CUATROCIENTOS", " QUINIENTOS",
        " SEISCIENTOS", " SETECIENTOS", " OCHOCIENTOS", " NOVECIENTOS",
    };

    /// <summary>
    /// Devuelve el monto en letras tal como lo imprimía el reporte de Access.
    /// </summary>
    /// <param name="monto">Monto en dólares, no negativo (Access le pasa <c>Abs(MainAmount)</c>).</param>
    /// <param name="corregirMenoresA2">Corrige el bug Q1 (montos menores a $2,00).</param>
    /// <param name="tamanoRelleno">Largo al que se rellena con «x» (90 en Access).</param>
    public static string ValorLetras(decimal monto, bool corregirMenoresA2 = false, int tamanoRelleno = TamanoPorDefecto)
    {
        if (monto < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(monto), "El monto en letras no admite negativos.");
        }

        // Round de VBA = redondeo bancario.
        monto = Math.Round(monto, 2, MidpointRounding.ToEven);
        if (monto >= 1_000_000_000m)
        {
            throw new ArgumentOutOfRangeException(nameof(monto), "El monto en letras admite hasta 999.999.999,99.");
        }

        var texto = monto == 0m ? "CERO DOLARES" : "";

        var entero = Math.Floor(monto);
        var millones = (int)(entero / 1_000_000m);
        var valor = entero - millones * 1_000_000m;
        var miles = (int)(valor / 1_000m);
        valor -= miles * 1_000m;
        var cientos = (int)(valor / 100m);
        valor -= cientos * 100m;
        var unidades = (int)valor;
        // «decimales» es un Integer en VBA: la conversión Double→Integer redondea.
        var decimales = (int)Math.Round((monto - entero) * 100m, 0, MidpointRounding.ToEven);

        if (millones > 0) texto += TMillones(millones);
        if (miles > 0) texto += TMiles(miles);
        if (cientos > 0) texto += TCientos(cientos, unidades > 0);
        if (unidades > 0) texto += " " + TUnidades(unidades);

        var agregaCentavos = corregirMenoresA2
            ? monto > 0m
            : millones > 0 || miles > 0 || cientos > 0 || unidades > 1;

        if (agregaCentavos)
        {
            texto += " CON " + decimales.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "/100   ";
            if (texto.Length < tamanoRelleno)
            {
                var faltan = tamanoRelleno - texto.Length;
                var sb = new StringBuilder(texto);
                for (var i = 1; i <= faltan; i++)
                {
                    if (i % 5 == 0)
                    {
                        sb.Append(' ');
                    }
                    sb.Append('x');
                }
                texto = sb.ToString();
            }
        }

        return texto.Trim();
    }

    /// <summary>
    /// ¿El texto en letras de este monto pierde los centavos por el bug Q1?
    /// (Positivo y menor a $2,00, o sea sin millones/miles/cientos y con unidades ≤ 1.)
    /// </summary>
    public static bool PierdeCentavosPorBugQ1(decimal monto)
    {
        monto = Math.Round(Math.Abs(monto), 2, MidpointRounding.ToEven);
        return monto > 0m && monto < 2m;
    }

    private static string TUnidades(int numero)
    {
        if (numero < 20)
        {
            return Unidad[numero - 1];
        }
        if (numero == 20)
        {
            return " VEINTE";
        }
        if (numero < 30)
        {
            return numero == 21
                ? " VEINTE Y " + Unidad[0]
                : " VEINTI" + Unidad[numero % 20 - 1];
        }

        var texto = Decena[numero / 10 - 3];
        if (numero % 10 != 0)
        {
            texto += " Y " + Unidad[numero % 10 - 1];
        }
        return texto;
    }

    private static string TCientos(int numero, bool hayUnidad)
        => numero == 1 && !hayUnidad ? " CIEN" : Ciento[numero - 1];

    private static string TMiles(int numero)
    {
        var cientos = numero / 100;
        numero -= cientos * 100;
        var unidades = numero;

        var texto = "";
        if (cientos > 0) texto += TCientos(cientos, unidades > 0);
        if (unidades > 0) texto += TUnidades(unidades);
        return texto + " MIL";
    }

    private static string TMillones(int numero)
    {
        var miles = numero / 1000;
        numero -= miles * 1000;
        var cientos = numero / 100;
        numero -= cientos * 100;
        var unidades = numero;

        var texto = "";
        if (miles > 0) texto = TMiles(miles);
        if (cientos > 0) texto += TCientos(cientos, unidades > 0);
        if (unidades > 0) texto += TUnidades(unidades);
        texto += " MILLON";
        if (miles > 0 || cientos > 0 || unidades > 1) texto += "ES ";
        return texto;
    }
}
