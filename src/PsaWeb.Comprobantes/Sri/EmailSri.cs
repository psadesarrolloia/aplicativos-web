using System.Text.RegularExpressions;

namespace PsaWeb.Comprobantes.Sri;

/// <summary>
/// Validación de correo con la misma expresión que el <c>EmailValidator</c>
/// original: debe coincidir con el patrón y no sobrar ningún carácter.
/// </summary>
public static partial class EmailSri
{
    [GeneratedRegex(@"\w+([-+.']\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*")]
    private static partial Regex Patron();

    public static bool EsValido(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        return Patron().IsMatch(email) && Patron().Replace(email, string.Empty).Length == 0;
    }
}
