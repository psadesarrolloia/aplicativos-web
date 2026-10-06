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
    private const string Emisor = MensajesCuenta.Producto;
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
    /// nuevos (muéstralos una sola vez).
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

        usuario.MetodoSegundoFactor = MetodosSegundoFactor.Totp;
        await _users.SetTwoFactorEnabledAsync(usuario, true); // también guarda MetodoSegundoFactor (UpdateAsync)
        var codigos = await _users.GenerateNewTwoFactorRecoveryCodesAsync(usuario, 10);
        return (true, codigos?.ToList() ?? new List<string>());
    }

    private const string PropositoActivarCorreo = "activar-2fa-correo";

    /// <summary>Método efectivo del segundo paso (las cuentas que lo activaron antes de existir el correo usan la app).</summary>
    public static string MetodoDe(UsuarioApp usuario) =>
        usuario.MetodoSegundoFactor == MetodosSegundoFactor.Correo ? MetodosSegundoFactor.Correo : MetodosSegundoFactor.Totp;

    /// <summary>Manda al correo del usuario un código para activar la verificación por correo. false si no tiene correo o no hay SMTP.</summary>
    public async Task<bool> EnviarCodigoActivacionCorreoAsync(UsuarioApp usuario, IEnviadorCorreoPlataforma correo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(usuario.Email) || !correo.Disponible) return false;
        var codigo = await _users.GenerateUserTokenAsync(usuario, TokenOptions.DefaultEmailProvider, PropositoActivarCorreo);
        var (asunto, html) = MensajesCuenta.CodigoVerificacion(usuario.NombreCompleto ?? usuario.UserName ?? "", codigo, "activar la verificación por correo");
        await correo.EnviarAsync(usuario.Email, asunto, html, ct);
        return true;
    }

    /// <summary>Activa la verificación en dos pasos por correo si el código es válido (y deja el correo como confirmado).</summary>
    public async Task<(bool Ok, IReadOnlyList<string> CodigosRecuperacion)> ActivarPorCorreoAsync(UsuarioApp usuario, string codigo)
    {
        var limpio = (codigo ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
        if (!await _users.VerifyUserTokenAsync(usuario, TokenOptions.DefaultEmailProvider, PropositoActivarCorreo, limpio))
        {
            return (false, Array.Empty<string>());
        }

        usuario.EmailConfirmed = true; // el proveedor "Email" de Identity solo emite códigos de ingreso a correos confirmados
        usuario.MetodoSegundoFactor = MetodosSegundoFactor.Correo;
        await _users.SetTwoFactorEnabledAsync(usuario, true);
        var codigos = await _users.GenerateNewTwoFactorRecoveryCodesAsync(usuario, 10);
        return (true, codigos?.ToList() ?? new List<string>());
    }

    /// <summary>Manda el código de ingreso por correo (segundo paso del login de quien eligió correo).</summary>
    public async Task<bool> EnviarCodigoIngresoAsync(UsuarioApp usuario, IEnviadorCorreoPlataforma correo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(usuario.Email) || !correo.Disponible) return false;
        var codigo = await _users.GenerateTwoFactorTokenAsync(usuario, TokenOptions.DefaultEmailProvider);
        var (asunto, html) = MensajesCuenta.CodigoVerificacion(usuario.NombreCompleto ?? usuario.UserName ?? "", codigo, "ingresar");
        await correo.EnviarAsync(usuario.Email, asunto, html, ct);
        return true;
    }

    public async Task<IReadOnlyList<string>> RegenerarCodigosRecuperacionAsync(UsuarioApp usuario)
    {
        var codigos = await _users.GenerateNewTwoFactorRecoveryCodesAsync(usuario, 10);
        return codigos?.ToList() ?? new List<string>();
    }

    public async Task DesactivarAsync(UsuarioApp usuario)
    {
        usuario.MetodoSegundoFactor = null;
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
