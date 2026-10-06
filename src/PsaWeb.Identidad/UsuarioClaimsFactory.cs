using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace PsaWeb.Identidad;

/// <summary>Claims propios de la cookie de sesión (se refrescan al re-firmar o cuando el validador del sello de seguridad la renueva).</summary>
public static class ClaimsPsa
{
    /// <summary>Perfil del panel (<see cref="Perfiles"/>). Los de <c>Plataforma:Admins</c> cuentan como Super Admin.</summary>
    public const string Perfil = "psa:perfil";

    /// <summary>"1" si la verificación en dos pasos está activa.</summary>
    public const string SegundoFactor = "psa:2fa";

    public static string PerfilDe(ClaimsPrincipal usuario) => usuario.FindFirstValue(Perfil) ?? Perfiles.Consulta;

    public static bool TieneSegundoFactor(ClaimsPrincipal usuario) => usuario.FindFirstValue(SegundoFactor) == "1";

    /// <summary>Perfil con panel (Super Admin / Admin) que todavía no activó la verificación en dos pasos: solo puede usar /mi-cuenta.</summary>
    public static bool DebeActivarSegundoFactor(ClaimsPrincipal usuario) =>
        usuario.Identity?.IsAuthenticated == true
        && Perfiles.ExigeSegundoFactor(PerfilDe(usuario))
        && !TieneSegundoFactor(usuario);
}

public sealed class UsuarioClaimsFactory : UserClaimsPrincipalFactory<UsuarioApp>
{
    private readonly IOptions<PlataformaOptions> _plataforma;

    public UsuarioClaimsFactory(UserManager<UsuarioApp> users, IOptions<IdentityOptions> options, IOptions<PlataformaOptions> plataforma)
        : base(users, options) => _plataforma = plataforma;

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(UsuarioApp user)
    {
        var id = await base.GenerateClaimsAsync(user);
        id.AddClaim(new Claim(ClaimsPsa.Perfil, PerfilEfectivo(user, _plataforma.Value)));
        id.AddClaim(new Claim(ClaimsPsa.SegundoFactor, user.TwoFactorEnabled ? "1" : "0"));
        return id;
    }

    /// <summary>El perfil guardado, salvo que el usuario esté en <c>Plataforma:Admins</c> (respaldo de emergencia) → Super Admin.</summary>
    public static string PerfilEfectivo(UsuarioApp user, PlataformaOptions plataforma) =>
        plataforma.EsAdmin(user.UserName) ? Perfiles.SuperAdmin : (Perfiles.Valido(user.Perfil) ? user.Perfil : Perfiles.Consulta);
}
