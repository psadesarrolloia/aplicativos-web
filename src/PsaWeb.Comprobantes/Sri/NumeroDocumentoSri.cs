namespace PsaWeb.Comprobantes.Sri;

/// <summary>
/// Analiza un número de comprobante del SRI (<c>000-000-000000000</c>).
/// Port de <c>NumberFormatValidate</c>: expone el código de establecimiento,
/// el punto de emisión y el secuencial, y si el número fue "corregido".
/// </summary>
public sealed class NumeroDocumentoSri
{
    public bool EsValido { get; private init; }
    public bool FueCorregido { get; private init; }
    public string CodigoEstablecimiento { get; private init; } = string.Empty;
    public string PuntoEmision { get; private init; } = string.Empty;
    public string Secuencial { get; private init; } = string.Empty;

    public string Completo =>
        string.IsNullOrEmpty(CodigoEstablecimiento) ? string.Empty
        : $"{CodigoEstablecimiento}-{PuntoEmision}-{Secuencial}";

    private static NumeroDocumentoSri Invalido(string est = "", string pto = "", string sec = "") =>
        new() { EsValido = false, CodigoEstablecimiento = est, PuntoEmision = pto, Secuencial = sec };

    /// <summary>Formato estricto: 17 caracteres, <c>000-000-000000000</c>, guiones en la posición 3 y 7.</summary>
    public static NumeroDocumentoSri AnalizarEstricto(string? numero)
    {
        numero ??= string.Empty;
        var sinGuiones = numero.Replace("-", "");

        var ok = numero.Length == 17
                 && sinGuiones.All(char.IsDigit)
                 && numero.IndexOf('-') == 3
                 && numero[4..].IndexOf('-') == 3;

        if (ok)
        {
            return new NumeroDocumentoSri
            {
                EsValido = true,
                CodigoEstablecimiento = numero[..3],
                PuntoEmision = numero.Substring(4, 3),
                Secuencial = numero[8..],
            };
        }

        // Igual que el original: si tiene largo suficiente, igual rellena los tramos (pero inválido).
        return numero.Length > 8
            ? Invalido(numero[..3], numero.Substring(4, 3), numero[8..])
            : Invalido();
    }

    /// <summary>
    /// Formato de factura: acepta 17 exacto, o largos con 2 guiones y &gt; 8 caracteres,
    /// corrigiendo el secuencial a 9 dígitos (<c>FueCorregido = true</c>).
    /// </summary>
    public static NumeroDocumentoSri AnalizarFactura(string? numero)
    {
        numero ??= string.Empty;
        var sinGuiones = numero.Replace("-", "");
        var guiones = numero.Length - sinGuiones.Length;

        if (numero.Length == 17)
        {
            return AnalizarEstricto(numero);
        }

        if (guiones == 2 && numero.Length > 8 && sinGuiones.All(char.IsDigit))
        {
            if (numero.Length > 7 && numero[3] == '-' && numero[7] == '-'
                && int.TryParse(numero[8..], out var sec) && sec > 0)
            {
                return new NumeroDocumentoSri
                {
                    EsValido = true,
                    FueCorregido = true,
                    CodigoEstablecimiento = numero[..3],
                    PuntoEmision = numero.Substring(4, 3),
                    Secuencial = sec.ToString("000000000"),
                };
            }
        }

        return Invalido();
    }

    /// <summary>
    /// Normaliza un número de factura al formato <c>000-000-000000000</c> cuando el
    /// secuencial viene corto pero es numérico (port de <c>SolveLongPurchaseNumber</c>).
    /// Si no puede, devuelve el número original.
    /// </summary>
    public static string NormalizarNumeroFactura(string? numero)
    {
        numero ??= string.Empty;
        var estricto = AnalizarEstricto(numero);
        if (estricto.EsValido)
        {
            return numero;
        }

        if (!string.IsNullOrEmpty(estricto.Secuencial) && estricto.Secuencial.All(char.IsDigit)
            && long.TryParse(estricto.Secuencial, out var sec))
        {
            var candidato = $"{estricto.CodigoEstablecimiento}-{estricto.PuntoEmision}-{sec:000000000}";
            return AnalizarEstricto(candidato).EsValido ? candidato : numero;
        }

        return numero;
    }
}
