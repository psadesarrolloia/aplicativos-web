using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PsaWeb.Modules.Reportes.Comun;

/// <summary>Una fila de configuración por (RUC, reporte). El logo viaja en la fila «empresa».</summary>
public class ConfiguracionReporteEntidad
{
    public int Id { get; set; }
    public string Ruc { get; set; } = "";
    public string Reporte { get; set; } = "";
    public string Json { get; set; } = "{}";
    public byte[]? Logo { get; set; }
    public string? LogoTipo { get; set; }
    public string? ActualizadoPor { get; set; }
    public DateTime ActualizadoEn { get; set; }
}

/// <summary>
/// Configuración de los reportes (Cartera / Bancos). Misma base física que
/// <c>PsaWebPlataforma</c> — igual que <c>ConciliacionDbContext</c>.
/// </summary>
public class ReportesDbContext(DbContextOptions<ReportesDbContext> options) : DbContext(options)
{
    public DbSet<ConfiguracionReporteEntidad> ConfiguracionesReporte => Set<ConfiguracionReporteEntidad>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        var e = builder.Entity<ConfiguracionReporteEntidad>();
        e.ToTable("ConfiguracionesReporte");
        e.Property(x => x.Ruc).HasMaxLength(20);
        e.Property(x => x.Reporte).HasMaxLength(30);
        e.Property(x => x.LogoTipo).HasMaxLength(50);
        e.Property(x => x.ActualizadoPor).HasMaxLength(100);
        e.HasIndex(x => new { x.Ruc, x.Reporte }).IsUnique();
    }
}

/// <summary>Fábrica para <c>dotnet ef</c> (design-time) — misma cadena que <c>PsaWebPlataforma</c>.</summary>
public sealed class ReportesDbContextFactory : IDesignTimeDbContextFactory<ReportesDbContext>
{
    public ReportesDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("PSAWEB_PLATAFORMA_CS")
                 ?? @"Server=.\SQLEXPRESS;Database=PsaWebPlataforma;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ReportesDbContext>()
            .UseSqlServer(cs)
            .Options;
        return new ReportesDbContext(options);
    }
}
