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

/// <summary>
/// Token de API para que una integración externa (la extensión de Chrome del
/// módulo de Conciliación SRI) se autentique contra el Host sin usar la cookie
/// de sesión. Un solo token activo por usuario: generar uno nuevo revoca el
/// anterior. Se guarda solo el hash del secreto, nunca el token completo.
/// </summary>
public class TokenExtension
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(450)]
    public string UsuarioId { get; set; } = string.Empty;

    /// <summary>8 caracteres hex — permite ubicar el token por índice sin recorrer todos los hashes.</summary>
    [MaxLength(8)]
    public string Prefijo { get; set; } = string.Empty;

    /// <summary>SHA-256 (hex) del secreto. Nunca se guarda el secreto en texto plano.</summary>
    [MaxLength(64)]
    public string HashSecreto { get; set; } = string.Empty;

    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UltimoUsoUtc { get; set; }
    public DateTime? RevocadoUtc { get; set; }
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
