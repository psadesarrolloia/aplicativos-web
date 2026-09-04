using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using QRCoder;

namespace PsaWeb.Identidad;

/// <summary>Estado de 2FA de un usuario para la pantalla de seguridad.</summary>
public sealed record EstadoSegundoFactor(bool Habilitado, int CodigosRecuperacionRestantes);

/// <summary>Datos para enrolar el autenticador: clave manual + QR (SVG) + uri otpauth.</summary>
public sealed record EnrolamientoTotp(string ClaveFormateada, string ClavePlana, string OtpAuthUri, string QrSvg);

/// <summary>
/// Operaciones de segundo factor (TOTP) sobre <see cref="UserManager{TUser}"/>:
/// generar clave, activar con un código, desactivar, regenerar códigos de
/// recuperación.
/// </summary>
public sealed class GestorSegundoFactor
{
    private const string Emisor = "Aplicativos web PSA";
    private readonly UserManager<UsuarioApp> _users;

    public GestorSegundoFactor(UserManager<UsuarioApp> users) => _users = users;

    public async Task<EstadoSegundoFactor> EstadoAsync(UsuarioApp usuario)
        => new(
            await _users.GetTwoFactorEnabledAsync(usuario),
            await _users.CountRecoveryCodesAsync(usuario));

    /// <summary>Genera (o recupera) la clave del autenticador y arma el QR.</summary>
    public async Task<EnrolamientoTotp> PrepararEnrolamientoAsync(UsuarioApp usuario)
    {
        var clave = await _users.GetAuthenticatorKeyAsync(usuario);
        if (string.IsNullOrEmpty(clave))
        {
            await _users.ResetAuthenticatorKeyAsync(usuario);
            clave = await _users.GetAuthenticatorKeyAsync(usuario);
        }

        var cuenta = await _users.GetEmailAsync(usuario) ?? usuario.UserName ?? "usuario";
        var uri = string.Format(
            "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
            UrlEncoder.Default.Encode(Emisor),
            UrlEncoder.Default.Encode(cuenta),
            clave);

        using var generador = new QRCodeGenerator();
        using var datos = generador.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        var svg = new SvgQRCode(datos).GetGraphic(4, darkColorHex: "#163154", lightColorHex: "#ffffff");

        return new EnrolamientoTotp(FormatearClave(clave!), clave!, uri, svg);
    }

    /// <summary>
    /// Activa el 2FA si el código es válido. Devuelve los códigos de recuperación
    /// nuevos (mostralos una sola vez).
    /// </summary>
    public async Task<(bool Ok, IReadOnlyList<string> CodigosRecuperacion)> ActivarAsync(
        UsuarioApp usuario, string codigo)
    {
        var limpio = (codigo ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);

        var valido = await _users.VerifyTwoFactorTokenAsync(
            usuario, _users.Options.Tokens.AuthenticatorTokenProvider, limpio);
        if (!valido)
        {
            return (false, Array.Empty<string>());
        }

        await _users.SetTwoFactorEnabledAsync(usuario, true);
        var codigos = await _users.GenerateNewTwoFactorRecoveryCodesAsync(usuario, 10);
        return (true, codigos?.ToList() ?? new List<string>());
    }

    public async Task<IReadOnlyList<string>> RegenerarCodigosRecuperacionAsync(UsuarioApp usuario)
    {
        var codigos = await _users.GenerateNewTwoFactorRecoveryCodesAsync(usuario, 10);
        return codigos?.ToList() ?? new List<string>();
    }

    public async Task DesactivarAsync(UsuarioApp usuario)
    {
        await _users.SetTwoFactorEnabledAsync(usuario, false);
        await _users.ResetAuthenticatorKeyAsync(usuario);
    }

    private static string FormatearClave(string clave)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < clave.Length; i += 4)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(clave.AsSpan(i, Math.Min(4, clave.Length - i)));
        }
        return sb.ToString().ToLowerInvariant();
    }
}
