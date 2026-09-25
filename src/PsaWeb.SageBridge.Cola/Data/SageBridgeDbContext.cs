using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PsaWeb.SageBridge.Cola.Data;

/// <summary>
/// Cola y estado del Sage Bridge, en la misma base física <c>PsaWebPlataforma</c> (como Conciliación y Reportes).
/// Este contexto es el dueño del esquema; el Bridge usa las mismas tablas por SQL directo.
/// </summary>
public class SageBridgeDbContext(DbContextOptions<SageBridgeDbContext> options) : DbContext(options)
{
    public DbSet<TrabajoSage> Trabajos => Set<TrabajoSage>();
    public DbSet<LatidoBridge> Latidos => Set<LatidoBridge>();
    public DbSet<EmpresaBridge> Empresas => Set<EmpresaBridge>();
    public DbSet<AuditoriaRegistroSage> Auditoria => Set<AuditoriaRegistroSage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<TrabajoSage>(t =>
        {
            t.ToTable("TrabajosSage");
            t.HasIndex(x => new { x.Ruc, x.Tipo, x.ClaveIdempotencia }).IsUnique();
            t.HasIndex(x => new { x.Estado, x.NoAntesDeUtc });
            t.HasIndex(x => new { x.Ruc, x.Estado });
            t.HasIndex(x => x.CreadoUtc);
        });
        builder.Entity<LatidoBridge>().ToTable("LatidosBridge");
        builder.Entity<EmpresaBridge>().ToTable("EmpresasBridge");
        builder.Entity<AuditoriaRegistroSage>(a =>
        {
            a.ToTable("AuditoriaRegistrosSage");
            a.HasIndex(x => new { x.Ruc, x.Documento });
            a.HasIndex(x => x.TrabajoId);
            a.HasIndex(x => x.FechaUtc);
        });
    }
}

/// <summary>Fábrica para <c>dotnet ef</c> (design-time) — misma cadena que <c>PsaWebPlataforma</c>.</summary>
public sealed class SageBridgeDbContextFactory : IDesignTimeDbContextFactory<SageBridgeDbContext>
{
    public SageBridgeDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("PSAWEB_PLATAFORMA_CS")
                 ?? @"Server=.\SQLEXPRESS;Database=PsaWebPlataforma;Trusted_Connection=True;TrustServerCertificate=True";
        return new SageBridgeDbContext(new DbContextOptionsBuilder<SageBridgeDbContext>().UseSqlServer(cs).Options);
    }
}
