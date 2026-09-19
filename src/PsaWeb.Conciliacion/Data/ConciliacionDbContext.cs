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

    /// <summary>Solicitudes de anulación de comprobantes emitidos y su seguimiento.</summary>
    public DbSet<SolicitudAnulacionComprobante> SolicitudesAnulacion => Set<SolicitudAnulacionComprobante>();

    /// <summary>Hallazgos de la conciliación marcados como "Aceptada / Revisada OK".</summary>
    public DbSet<RevisionConciliacion> RevisionesConciliacion => Set<RevisionConciliacion>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<RevisionConciliacion>()
            .HasIndex(r => new { r.Ruc, r.Clave })
            .IsUnique();
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

        // A lo sumo UNA solicitud abierta por comprobante (las cerradas se acumulan como historial).
        builder.Entity<SolicitudAnulacionComprobante>()
            .HasIndex(s => new { s.Ruc, s.CodDoc, s.RefId })
            .IsUnique()
            .HasFilter("[Estado] = 'Solicitada'");
        builder.Entity<SolicitudAnulacionComprobante>()
            .HasIndex(s => new { s.Ruc, s.CodDoc, s.RefId });
        builder.Entity<SolicitudAnulacionComprobante>()
            .HasIndex(s => s.Estado);
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
