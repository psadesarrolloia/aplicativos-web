using System.Security.Cryptography;
using System.Text;

namespace PsaWeb.Identidad;

/// <summary>
/// Genera y valida el formato del token de la extensión de Chrome
/// (`psaext_&lt;prefijo de 8&gt;&lt;secreto de 48&gt;`, todo hex). Es alta entropía por
/// sí solo — a diferencia de una contraseña, no hace falta un hasher lento con
/// sal (PBKDF2/bcrypt): alcanza con SHA-256 simple del secreto.
/// </summary>
public static class GeneradorTokenExtension
{
    private const string Encabezado = "psaext_";
    private const int LargoPrefijo = 8;   // 4 bytes en hex
    private const int LargoSecreto = 48;  // 24 bytes en hex

    /// <summary>Genera un token nuevo. Devuelve el token completo (mostrar una sola vez) + lo que se persiste.</summary>
    public static (string TokenCompleto, string Prefijo, string HashSecreto) Generar()
    {
        var prefijo = Convert.ToHexString(RandomNumberGenerator.GetBytes(LargoPrefijo / 2)).ToLowerInvariant();
        var secreto = Convert.ToHexString(RandomNumberGenerator.GetBytes(LargoSecreto / 2)).ToLowerInvariant();
        var tokenCompleto = $"{Encabezado}{prefijo}{secreto}";
        return (tokenCompleto, prefijo, Hash(secreto));
    }

    /// <summary>SHA-256 del secreto, en hex minúscula.</summary>
    public static string Hash(string secreto) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secreto))).ToLowerInvariant();

    /// <summary>Separa un token recibido en prefijo (para buscar) + secreto (para comparar el hash).</summary>
    public static bool TryDescomponer(string? tokenCompleto, out string prefijo, out string secreto)
    {
        prefijo = string.Empty;
        secreto = string.Empty;
        if (string.IsNullOrEmpty(tokenCompleto) || !tokenCompleto.StartsWith(Encabezado, StringComparison.Ordinal))
        {
            return false;
        }

        var resto = tokenCompleto[Encabezado.Length..];
        if (resto.Length != LargoPrefijo + LargoSecreto)
        {
            return false;
        }

        prefijo = resto[..LargoPrefijo];
        secreto = resto[LargoPrefijo..];
        return true;
    }

    /// <summary>Compara dos hashes hex en tiempo constante (evita timing attacks).</summary>
    public static bool HashesIguales(string hashA, string hashB)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hashA), Convert.FromHexString(hashB));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
