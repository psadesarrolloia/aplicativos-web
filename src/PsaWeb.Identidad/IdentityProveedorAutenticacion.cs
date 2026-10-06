using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace PsaWeb.Identidad;

/// <summary>
/// Implementación de <see cref="IProveedorAutenticacion"/> con ASP.NET Core
/// Identity local (<see cref="SignInManager{TUser}"/>). Lockout activado en cada
/// fallo; la política de claves y el tiempo de bloqueo se configuran en
/// <c>AddIdentidadPlataforma</c>. Cada intento queda en la auditoría.
/// Se ingresa con el correo (o, en la transición, con el usuario interno). El segundo paso es con la app (TOTP), con un código
/// por correo o con un código de recuperación.
/// </summary>
public sealed class IdentityProveedorAutenticacion : IProveedorAutenticacion
{
    private readonly SignInManager<UsuarioApp> _signIn;
    private readonly UserManager<UsuarioApp> _users;
    private readonly AuditoriaAuth _auditoria;
    private readonly GestorSegundoFactor _segundoFactor;
    private readonly IEnviadorCorreoPlataforma _correo;
    private readonly ILogger<IdentityProveedorAutenticacion> _logger;

    public IdentityProveedorAutenticacion(
        SignInManager<UsuarioApp> signIn, UserManager<UsuarioApp> users, AuditoriaAuth auditoria,
        GestorSegundoFactor segundoFactor, IEnviadorCorreoPlataforma correo, ILogger<IdentityProveedorAutenticacion> logger)
    {
        _signIn = signIn;
        _users = users;
        _auditoria = auditoria;
        _segundoFactor = segundoFactor;
        _correo = correo;
        _logger = logger;
    }

    /// <summary>Busca por correo si el texto lo parece; si no (o no aparece), por usuario interno.</summary>
    public async Task<UsuarioApp?> BuscarAsync(string? correoOUsuario)
    {
        var texto = (correoOUsuario ?? string.Empty).Trim();
        if (texto.Length == 0) return null;
        if (texto.Contains('@') && await _users.FindByEmailAsync(texto) is { } porCorreo) return porCorreo;
        return await _users.FindByNameAsync(texto);
    }

    public async Task<ResultadoLogin> IniciarSesionAsync(
        string usuario, string clave, bool recordarme, CancellationToken cancellationToken = default)
    {
        var user = await BuscarAsync(usuario);
        if (user is null)
        {
            await _auditoria.RegistrarAsync(TiposEventoAuth.LoginFallido, usuario, "usuario inexistente", cancellationToken);
            return ResultadoLogin.Invalido();
        }
        if (!user.Activo)
        {
            await _auditoria.RegistrarAsync(TiposEventoAuth.LoginDeshabilitado, user.UserName, null, cancellationToken);
            return ResultadoLogin.Deshabilitado();
        }

        var r = await _signIn.PasswordSignInAsync(user, clave, recordarme, lockoutOnFailure: true);
        var resultado = await MapearAsync(r, user);
        if (r.Succeeded)
        {
            await AlIngresarAsync(user, TiposEventoAuth.LoginOk, cancellationToken);
        }
        else
        {
            await RegistrarResultadoLoginAsync(user.UserName, r, cancellationToken);
        }
        return resultado;
    }

    public async Task<string?> MetodoSegundoFactorPendienteAsync()
    {
        var user = await _signIn.GetTwoFactorAuthenticationUserAsync();
        return user is null ? null : GestorSegundoFactor.MetodoDe(user);
    }

    public async Task<bool> EnviarCodigoSegundoFactorAsync(CancellationToken cancellationToken = default)
    {
        var user = await _signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null || GestorSegundoFactor.MetodoDe(user) != MetodosSegundoFactor.Correo) return false;

        // Un código por minuto como mucho (recargar la página no debe llenar el buzón).
        var cache = _signIn.Context.RequestServices.GetService(typeof(Microsoft.Extensions.Caching.Memory.IMemoryCache))
            as Microsoft.Extensions.Caching.Memory.IMemoryCache;
        var clave = "2fa-correo:" + user.Id;
        if (cache is not null && cache.TryGetValue(clave, out _)) return true;
        try
        {
            var enviado = await _segundoFactor.EnviarCodigoIngresoAsync(user, _correo, cancellationToken);
            if (enviado) cache?.Set(clave, true, TimeSpan.FromMinutes(1));
            return enviado;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo enviar el código de ingreso por correo a {Usuario}", user.UserName);
            return false;
        }
    }

    public async Task<ResultadoLogin> VerificarSegundoFactorAsync(
        string codigoTotp, bool recordarDispositivo, CancellationToken cancellationToken = default)
    {
        var user = await _signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            // La cookie del primer paso venció (5 min): hay que volver a poner la clave.
            return ResultadoLogin.Invalido("La verificación venció. Ingresa de nuevo con tu correo y contraseña.");
        }

        var limpio = (codigoTotp ?? string.Empty).Replace(" ", string.Empty).Trim();
        SignInResult r;
        string? via = null;
        if (EsCodigoDeSeisDigitos(limpio.Replace("-", string.Empty)))
        {
            limpio = limpio.Replace("-", string.Empty);
            r = GestorSegundoFactor.MetodoDe(user) == MetodosSegundoFactor.Correo
                ? await _signIn.TwoFactorSignInAsync(TokenOptions.DefaultEmailProvider, limpio, recordarDispositivo, recordarDispositivo)
                : await _signIn.TwoFactorAuthenticatorSignInAsync(limpio, recordarDispositivo, recordarDispositivo);
        }
        else
        {
            // Código de recuperación (los de Identity tienen la forma XXXXX-XXXXX). Cada uno sirve una sola vez.
            r = await _signIn.TwoFactorRecoveryCodeSignInAsync(limpio);
            via = "con código de recuperación";
        }

        if (r.Succeeded)
        {
            await AlIngresarAsync(user, TiposEventoAuth.SegundoFactorOk, cancellationToken, via);
        }
        else
        {
            await _auditoria.RegistrarAsync(
                r.IsLockedOut ? TiposEventoAuth.Bloqueo : TiposEventoAuth.SegundoFactorFallido, user.UserName, via, cancellationToken);
        }

        return await MapearAsync(r, user);
    }

    public async Task CerrarSesionAsync()
    {
        var usuario = _signIn.Context.User.Identity?.Name;
        await _signIn.SignOutAsync();
        await _auditoria.RegistrarAsync(TiposEventoAuth.Logout, usuario);
    }

    private static bool EsCodigoDeSeisDigitos(string s) => s.Length == 6 && s.All(char.IsAsciiDigit);

    /// <summary>Ingreso completo: aviso de IP nueva (antes de registrar el éxito, para comparar contra el historial), auditoría y último acceso.</summary>
    private async Task AlIngresarAsync(UsuarioApp user, string tipo, CancellationToken ct, string? detalle = null)
    {
        var ip = _auditoria.IpActual;
        var ipNueva = await _auditoria.EsIpNuevaAsync(user.UserName ?? string.Empty, ip, ct);

        await _auditoria.RegistrarAsync(tipo, user.UserName, detalle, ct);

        try
        {
            user.UltimoAccesoUtc = DateTime.UtcNow;
            await _users.UpdateAsync(user);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo guardar el último acceso de {Usuario}", user.UserName);
        }

        if (ipNueva)
        {
            await _auditoria.RegistrarAsync(TiposEventoAuth.LoginIpNueva, user.UserName, ip, ct);
            if (!string.IsNullOrWhiteSpace(user.Email) && _correo.Disponible)
            {
                try
                {
                    var (asunto, html) = MensajesCuenta.IngresoNuevo(user.NombreCompleto ?? user.UserName ?? "", ip ?? "?", DateTime.UtcNow);
                    await _correo.EnviarAsync(user.Email, asunto, html, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo avisar por correo el ingreso desde IP nueva de {Usuario}", user.UserName);
                }
            }
        }
    }

    private async Task RegistrarResultadoLoginAsync(string? usuario, SignInResult r, CancellationToken ct)
    {
        var tipo = r switch
        {
            { Succeeded: true } => TiposEventoAuth.LoginOk,
            { RequiresTwoFactor: true } => TiposEventoAuth.SegundoFactorPendiente,
            { IsLockedOut: true } => TiposEventoAuth.Bloqueo,
            { IsNotAllowed: true } => TiposEventoAuth.LoginDeshabilitado,
            _ => TiposEventoAuth.LoginFallido,
        };
        await _auditoria.RegistrarAsync(tipo, usuario, null, ct);
    }

    private async Task<ResultadoLogin> MapearAsync(SignInResult r, UsuarioApp? user)
    {
        if (r.Succeeded) return ResultadoLogin.Correcto;
        if (r.RequiresTwoFactor) return ResultadoLogin.PideSegundoFactor;
        if (r.IsNotAllowed) return ResultadoLogin.Deshabilitado();
        if (r.IsLockedOut)
        {
            TimeSpan? restante = null;
            if (user is not null)
            {
                var fin = await _users.GetLockoutEndDateAsync(user);
                if (fin is { } f && f > DateTimeOffset.UtcNow) restante = f - DateTimeOffset.UtcNow;
            }
            return ResultadoLogin.BloqueadoPor(restante);
        }
        return ResultadoLogin.Invalido();
    }
}
