using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PsaWeb.Conciliacion.Data;

/// <summary>
/// Staging de comprobantes del SRI. Vive en la misma base física
/// <c>PsaWebPlataforma</c> que Identity (no se provisiona una base nueva) —
/// EF Core no exige que una base tenga un solo <c>DbContext</c> dueño.
/// </summary>
public class ConciliacionDbContext(DbContextOptions<ConciliacionDbContext> options) : DbContext(options)
{
    public DbSet<ComprobanteSriDescargado> ComprobantesSriDescargados => Set<ComprobanteSriDescargado>();

    /// <summary>Estado en el SRI de los comprobantes que emitimos (facturas, retenciones, NC, liquidaciones).</summary>
    public DbSet<EstadoSriComprobante> EstadosSriComprobantes => Set<EstadoSriComprobante>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ComprobanteSriDescargado>()
            .HasIndex(c => new { c.Ruc, c.ClaveAcceso })
            .IsUnique();
        builder.Entity<ComprobanteSriDescargado>()
            .HasIndex(c => new { c.Ruc, c.FechaEmision });

        builder.Entity<EstadoSriComprobante>()
            .HasIndex(e => new { e.Ruc, e.CodDoc, e.RefId })
            .IsUnique();
        builder.Entity<EstadoSriComprobante>()
            .HasIndex(e => new { e.Ruc, e.ClaveAcceso });
        builder.Entity<EstadoSriComprobante>()
            .HasIndex(e => new { e.Ruc, e.FechaEmision });
    }
}

/// <summary>Fábrica para <c>dotnet ef</c> (design-time) — misma cadena que <c>PsaWebPlataforma</c>.</summary>
public sealed class ConciliacionDbContextFactory : IDesignTimeDbContextFactory<ConciliacionDbContext>
{
    public ConciliacionDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("PSAWEB_PLATAFORMA_CS")
                 ?? @"Server=.\SQLEXPRESS;Database=PsaWebPlataforma;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ConciliacionDbContext>()
            .UseSqlServer(cs)
            .Options;
        return new ConciliacionDbContext(options);
    }
}
