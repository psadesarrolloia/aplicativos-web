using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace PsaWeb.Identidad;

/// <summary>Usuario de la plataforma web (login local).</summary>
public class UsuarioApp : IdentityUser
{
    [MaxLength(120)]
    public string? NombreCompleto { get; set; }

    /// <summary>Si es false, no puede iniciar sesión aunque la clave sea correcta.</summary>
    public bool Activo { get; set; } = true;

    /// <summary>
    /// Vínculo con <c>users.username</c> de PeachEBills (para resolver empresas y
    /// permisos vía <c>ISecurityDirectory</c>). Normalmente igual a <see cref="IdentityUser.UserName"/>.
    /// </summary>
    [MaxLength(50)]
    public string PeachUsername { get; set; } = string.Empty;

    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Evento de autenticación para auditoría (se puebla en F-Shell-4).</summary>
public class EventoAuth
{
    public long Id { get; set; }
    public DateTime Utc { get; set; } = DateTime.UtcNow;

    [MaxLength(50)]
    public string Usuario { get; set; } = string.Empty;

    /// <summary>login-ok | login-fail | lockout | 2fa-fail | logout | cambio-empresa</summary>
    [MaxLength(30)]
    public string Tipo { get; set; } = string.Empty;

    [MaxLength(45)]
    public string? Ip { get; set; }

    [MaxLength(400)]
    public string? Detalle { get; set; }
}
