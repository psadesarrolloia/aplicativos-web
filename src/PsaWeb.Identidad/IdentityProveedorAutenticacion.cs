using Microsoft.AspNetCore.Identity;

namespace PsaWeb.Identidad;

/// <summary>
/// Implementación de <see cref="IProveedorAutenticacion"/> con ASP.NET Core
/// Identity local (<see cref="SignInManager{TUser}"/>). Lockout activado en cada
/// fallo; la política de claves y el tiempo de bloqueo se configuran en
/// <c>AddIdentidadPlataforma</c>. Cada intento queda en la auditoría.
/// </summary>
public sealed class IdentityProveedorAutenticacion : IProveedorAutenticacion
{
    private readonly SignInManager<UsuarioApp> _signIn;
    private readonly UserManager<UsuarioApp> _users;
    private readonly AuditoriaAuth _auditoria;

    public IdentityProveedorAutenticacion(
        SignInManager<UsuarioApp> signIn, UserManager<UsuarioApp> users, AuditoriaAuth auditoria)
    {
        _signIn = signIn;
        _users = users;
        _auditoria = auditoria;
    }

    public async Task<ResultadoLogin> IniciarSesionAsync(
        string usuario, string clave, bool recordarme, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByNameAsync(usuario) ?? await _users.FindByEmailAsync(usuario);
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
        await RegistrarResultadoLoginAsync(user.UserName, r, cancellationToken);
        return resultado;
    }

    public async Task<ResultadoLogin> VerificarSegundoFactorAsync(
        string codigoTotp, bool recordarDispositivo, CancellationToken cancellationToken = default)
    {
        var limpio = (codigoTotp ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
        var r = await _signIn.TwoFactorAuthenticatorSignInAsync(
            limpio, isPersistent: recordarDispositivo, rememberClient: recordarDispositivo);

        var usuario = _signIn.Context.User.Identity?.Name;
        await _auditoria.RegistrarAsync(
            r.Succeeded ? TiposEventoAuth.SegundoFactorOk : TiposEventoAuth.SegundoFactorFallido,
            usuario, null, cancellationToken);

        return await MapearAsync(r, user: null);
    }

    public async Task CerrarSesionAsync()
    {
        var usuario = _signIn.Context.User.Identity?.Name;
        await _signIn.SignOutAsync();
        await _auditoria.RegistrarAsync(TiposEventoAuth.Logout, usuario);
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
