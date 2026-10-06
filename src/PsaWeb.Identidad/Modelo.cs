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

    /// <summary>Perfil del panel: <see cref="Perfiles.SuperAdmin"/>, <see cref="Perfiles.Admin"/> o <see cref="Perfiles.Usuario"/>.</summary>
    [MaxLength(20)]
    public string Perfil { get; set; } = Perfiles.Usuario;

    /// <summary>Cómo hace el segundo paso si lo tiene activo: <see cref="MetodosSegundoFactor.Totp"/> o <see cref="MetodosSegundoFactor.Correo"/>.</summary>
    [MaxLength(10)]
    public string? MetodoSegundoFactor { get; set; }

    public DateTime? UltimoAccesoUtc { get; set; }
}

/// <summary>Perfiles del panel de administración (PLAN-ACCESOS-WEB §2).</summary>
public static class Perfiles
{
    /// <summary>Crea y desactiva usuarios, asigna accesos, auditoría, configuración.</summary>
    public const string SuperAdmin = "SuperAdmin";

    /// <summary>Activa, desactiva y reasigna empresas y módulos de usuarios existentes. No crea usuarios.</summary>
    public const string Admin = "Admin";

    public const string Usuario = "Usuario";

    public static readonly IReadOnlyList<string> Todos = new[] { SuperAdmin, Admin, Usuario };

    public static bool Valido(string? perfil) => perfil is not null && Todos.Contains(perfil);

    /// <summary>Los perfiles con panel deben tener la verificación en dos pasos activa.</summary>
    public static bool ExigeSegundoFactor(string? perfil) => perfil is SuperAdmin or Admin;

    public static string Etiqueta(string? perfil) => perfil switch
    {
        SuperAdmin => "Super Admin",
        Admin => "Admin",
        _ => "Sin panel",
    };
}

public static class MetodosSegundoFactor
{
    public const string Totp = "Totp";
    public const string Correo = "Correo";
}

/// <summary>
/// Acceso web de un usuario a una empresa (PLAN-ACCESOS-WEB §3). Independiente de PeachEBills: lo administra el panel.
/// </summary>
public class AccesoEmpresa
{
    [MaxLength(450)]
    public string UsuarioId { get; set; } = string.Empty;

    [MaxLength(13)]
    public string Ruc { get; set; } = string.Empty;

    /// <summary>false = la empresa queda asignada pero sin acceso (se conservan sus llaves para reactivarla).</summary>
    public bool Activo { get; set; } = true;

    /// <summary>Usuario de Sage 50 de esta persona en esta compañía (los usuarios de Sage son por compañía). Requisito de los módulos de escritura.</summary>
    [MaxLength(50)]
    public string? UsuarioSage { get; set; }

    [MaxLength(50)]
    public string ModificadoPor { get; set; } = string.Empty;

    public DateTime ModificadoUtc { get; set; } = DateTime.UtcNow;

    public List<AccesoLlave> Llaves { get; set; } = new();
}

/// <summary>Una llave de permiso (mismos códigos que <c>Permisos.cs</c> / <c>allowAction</c>) de un usuario en una empresa.</summary>
public class AccesoLlave
{
    [MaxLength(450)]
    public string UsuarioId { get; set; } = string.Empty;

    [MaxLength(13)]
    public string Ruc { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Llave { get; set; } = string.Empty;

    [MaxLength(50)]
    public string OtorgadoPor { get; set; } = string.Empty;

    public DateTime OtorgadoUtc { get; set; } = DateTime.UtcNow;
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
