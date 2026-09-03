using System.Security.Cryptography;
using System.Text;

namespace PsaWeb.PeachEbills;

/// <summary>
/// Descifra los secretos guardados en la base <c>PeachEBills</c> (contraseñas
/// ODBC de <c>PeachConnString</c>, etc.). Reproduce exactamente el
/// <c>PasswordSecurity</c> original: TripleDES / ECB / PKCS7 con la clave
/// = <c>MD5("1oo2435681")</c>.
/// </summary>
/// <remarks>
/// Cripto débil (clave embebida en el código, modo ECB). Se mantiene solo para
/// poder leer los datos ya existentes. Migrar el almacenamiento de secretos es
/// deuda técnica (ver BACKLOG).
/// </remarks>
public static class DbSecret
{
    private const string Key = "1oo2435681";

    private static TripleDES Create()
    {
        var tdes = TripleDES.Create();
        tdes.Key = MD5.HashData(Encoding.UTF8.GetBytes(Key));
        tdes.Mode = CipherMode.ECB;
        tdes.Padding = PaddingMode.PKCS7;
        return tdes;
    }

    public static string Decrypt(string? base64)
    {
        if (string.IsNullOrEmpty(base64))
        {
            return string.Empty;
        }

        using var tdes = Create();
        using var transform = tdes.CreateDecryptor();
        var input = Convert.FromBase64String(base64);
        var plain = transform.TransformFinalBlock(input, 0, input.Length);
        return Encoding.UTF8.GetString(plain);
    }

    public static string Encrypt(string plainText)
    {
        using var tdes = Create();
        using var transform = tdes.CreateEncryptor();
        var input = Encoding.UTF8.GetBytes(plainText);
        var cipher = transform.TransformFinalBlock(input, 0, input.Length);
        return Convert.ToBase64String(cipher);
    }
}
