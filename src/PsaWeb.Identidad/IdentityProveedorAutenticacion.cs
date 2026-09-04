using Microsoft.AspNetCore.Identity;

namespace PsaWeb.Identidad;

/// <summary>
/// Implementación de <see cref="IProveedorAutenticacion"/> con ASP.NET Core
/// Identity local (<see cref="SignInManager{TUser}"/>). Lockout activado en cada
/// fallo; la política de claves y el tiempo de bloqueo se configuran en
/// <c>AddIdentidadPlataforma</c>.
/// </summary>
public sealed class IdentityProveedorAutenticacion : IProveedorAutenticacion
{
    private readonly SignInManager<UsuarioApp> _signIn;
    private readonly UserManager<UsuarioApp> _users;

    public IdentityProveedorAutenticacion(SignInManager<UsuarioApp> signIn, UserManager<UsuarioApp> users)
    {
        _signIn = signIn;
        _users = users;
    }

    public async Task<ResultadoLogin> IniciarSesionAsync(
        string usuario, string clave, bool recordarme, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByNameAsync(usuario) ?? await _users.FindByEmailAsync(usuario);
        if (user is null)
        {
            return ResultadoLogin.Invalido();
        }
        if (!user.Activo)
        {
            return ResultadoLogin.Deshabilitado();
        }

        var r = await _signIn.PasswordSignInAsync(user, clave, recordarme, lockoutOnFailure: true);
        return await MapearAsync(r, user);
    }

    public async Task<ResultadoLogin> VerificarSegundoFactorAsync(
        string codigoTotp, bool recordarDispositivo, CancellationToken cancellationToken = default)
    {
        var limpio = (codigoTotp ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
        var r = await _signIn.TwoFactorAuthenticatorSignInAsync(
            limpio, isPersistent: recordarDispositivo, rememberClient: recordarDispositivo);
        return await MapearAsync(r, user: null);
    }

    public Task CerrarSesionAsync() => _signIn.SignOutAsync();

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
