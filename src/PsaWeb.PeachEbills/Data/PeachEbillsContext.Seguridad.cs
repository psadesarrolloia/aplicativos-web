using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

// DbSets de las tablas de seguridad heredadas. Se enganchan al contexto
// scaffolded vía la clase parcial + OnModelCreatingPartial.
public partial class PeachEbillsContext
{
    public virtual DbSet<SecUser> SecUsers { get; set; } = null!;
    public virtual DbSet<SecUserTransmitter> SecUserTransmitters { get; set; } = null!;
    public virtual DbSet<SecRole> SecRoles { get; set; } = null!;
    public virtual DbSet<SecUserRole> SecUserRoles { get; set; } = null!;
    public virtual DbSet<SecRolePermission> SecRolePermissions { get; set; } = null!;
    public virtual DbSet<SecPermission> SecPermissions { get; set; } = null!;

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SecUser>().ToTable("user");
    }
}
