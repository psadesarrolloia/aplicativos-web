using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PsaWeb.Identidad;

/// <summary>
/// Base <c>PsaWebPlataforma</c>: tablas de ASP.NET Core Identity + auditoría de
/// autenticación. Separada de <c>PeachEBills</c> (límite de seguridad y backup
/// propios).
/// </summary>
public class PlataformaDbContext : IdentityDbContext<UsuarioApp>
{
    public PlataformaDbContext(DbContextOptions<PlataformaDbContext> options) : base(options) { }

    public DbSet<EventoAuth> EventosAuth => Set<EventoAuth>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<UsuarioApp>().HasIndex(u => u.PeachUsername);
        builder.Entity<EventoAuth>().HasIndex(e => e.Utc);
        builder.Entity<EventoAuth>().HasIndex(e => e.Usuario);
    }
}

/// <summary>
/// Fábrica para <c>dotnet ef</c> (design-time). Toma la cadena de
/// <c>PSAWEB_PLATAFORMA_CS</c> o usa la copia local de PREDATOR por defecto.
/// </summary>
public sealed class PlataformaDbContextFactory : IDesignTimeDbContextFactory<PlataformaDbContext>
{
    public PlataformaDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("PSAWEB_PLATAFORMA_CS")
                 ?? @"Server=.\SQLEXPRESS;Database=PsaWebPlataforma;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseSqlServer(cs)
            .Options;
        return new PlataformaDbContext(options);
    }
}
