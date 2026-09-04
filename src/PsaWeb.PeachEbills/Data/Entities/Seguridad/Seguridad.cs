using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

// Tablas de seguridad heredadas de los aplicativos de escritorio
// (Sage50FacturacionElectronica / Sage50usIntegration usan estas mismas).
// Solo lectura desde la web, detrás de ISecurityDirectory.

[Table("user")]
public partial class SecUser
{
    [Key]
    [StringLength(50)]
    public string Username { get; set; } = null!;

    [StringLength(50)]
    public string? Email { get; set; }
}

/// <summary>Empresas (por RUC) a las que un usuario tiene acceso.</summary>
[Table("UserTransmitter")]
public partial class SecUserTransmitter
{
    [Key]
    [Column("udt id")]
    public int UdtId { get; set; }

    [Column("user")]
    [StringLength(50)]
    public string User { get; set; } = null!;

    [Column("RUC")]
    [StringLength(13)]
    public string Ruc { get; set; } = null!;

    [Column("Order")]
    public int Order { get; set; }
}

[Table("roles")]
public partial class SecRole
{
    [Key]
    [Column("rolid")]
    public int RolId { get; set; }

    [Column("rolName")]
    [StringLength(50)]
    public string RolName { get; set; } = null!;
}

/// <summary>Rol que tiene un usuario en una empresa concreta.</summary>
[Table("udrUserRolesTr")]
public partial class SecUserRole
{
    [Key]
    [Column("udrid")]
    public int UdrId { get; set; }

    [Column("udruser")]
    [StringLength(50)]
    public string User { get; set; } = null!;

    [Column("udrrol")]
    public int Rol { get; set; }

    [Column("udrRucTransmitter")]
    [StringLength(13)]
    public string Ruc { get; set; } = null!;
}

/// <summary>Permiso (código) que otorga un rol.</summary>
[Table("adrAllowRol")]
public partial class SecRolePermission
{
    [Key]
    [Column("adrId")]
    public int AdrId { get; set; }

    [Column("adrRol")]
    public int Rol { get; set; }

    [Column("adraid")]
    public int AllowActionId { get; set; }

    [Column("adrAllowCode")]
    [StringLength(10)]
    public string AllowCode { get; set; } = null!;

    [Column("adrActive")]
    public bool Active { get; set; }
}

/// <summary>Catálogo de permisos (código + nombre legible).</summary>
[Table("allowAction")]
public partial class SecPermission
{
    [Key]
    [Column("aid")]
    public int Aid { get; set; }

    [Column("allowName")]
    [StringLength(50)]
    public string AllowName { get; set; } = null!;

    [Column("allowCode")]
    [StringLength(10)]
    public string AllowCode { get; set; } = null!;
}
