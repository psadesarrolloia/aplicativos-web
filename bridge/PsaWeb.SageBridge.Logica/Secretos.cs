using System;
using System.Security.Cryptography;
using System.Text;

namespace PsaWeb.SageBridge.Logica;

/// <summary>
/// Cifra la clave de aplicación de Sage con DPAPI a nivel de EQUIPO: la puede descifrar cualquier proceso de ese
/// servidor (incluida la cuenta del servicio), pero el valor copiado a otro equipo no sirve.
/// </summary>
internal static class Secretos
{
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("PsaWeb.SageBridge/clave-aplicacion-sage");

    public static string Proteger(string texto) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(texto), Entropia, DataProtectionScope.LocalMachine));

    public static string Desproteger(string base64)
    {
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(base64), Entropia, DataProtectionScope.LocalMachine));
        }
        catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
        {
            throw new InvalidOperationException(
                "No se pudo descifrar ClaveAplicacionProtegida: se generó en otro equipo o está dañada. Vuelve a correr --proteger-clave en este servidor.", ex);
        }
    }
}
