using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PsaWeb.Modules.Ventas.Prefacturas;

public class PrefacturaEntidad
{
    public int Id { get; set; }
    public string Ruc { get; set; } = "";
    public string EmpresaNombre { get; set; } = "";
    public int Numero { get; set; }
    public DateTime FechaEmision { get; set; }
    public DateTime ValidaHasta { get; set; }
    public int Estado { get; set; }
    public string ClienteId { get; set; } = "";
    public string ClienteNombre { get; set; } = "";
    public string ClienteContacto { get; set; } = "";
    public string ClienteTelefono { get; set; } = "";
    public string ClienteEmail { get; set; } = "";
    public int ListaDePrecios { get; set; }
    public int DiasCredito { get; set; }
    public string Terminos { get; set; } = "";
    public string Vendedor { get; set; } = "";
    public string Etiqueta { get; set; } = "";
    public string OrdenCliente { get; set; } = "";
    public string DireccionEnvio { get; set; } = "";
    public string NotaCliente { get; set; } = "";
    public string NotaInterna { get; set; } = "";
    public string CodigoImpuesto { get; set; } = "";
    public decimal PorcentajeIva { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
    public string CreadaPor { get; set; } = "";
    public DateTime CreadaEn { get; set; }
    public int CorreoEstado { get; set; }
    public string CorreoDestinatarios { get; set; } = "";
    public string? CorreoError { get; set; }
    public DateTime? CorreoEnviadoEn { get; set; }
    public string? FacturaSage { get; set; }
    public string? FacturadaPor { get; set; }
    public DateTime? FacturadaEn { get; set; }
    public List<PrefacturaLineaEntidad> Lineas { get; set; } = new();
}

public class PrefacturaLineaEntidad
{
    public int Id { get; set; }
    public int PrefacturaId { get; set; }
    public int Orden { get; set; }
    public string ItemId { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string UnidadMedida { get; set; } = "";
    public decimal Cantidad { get; set; }
    public decimal? PrecioLista { get; set; }
    public decimal PrecioUnitario { get; set; }
    public bool PrecioManual { get; set; }
    public decimal Monto { get; set; }
    public decimal? ExistenciaAlEmitir { get; set; }
}

public class ConfiguracionVentasEntidad
{
    public string Ruc { get; set; } = "";
    public string CorreoContabilidad { get; set; } = "";
    public string CorreoAdicional { get; set; } = "";
    public int VigenciaDias { get; set; } = 15;
    public string CodigoImpuesto { get; set; } = "4-15%";
    public decimal PorcentajeIva { get; set; } = 15m;
    public string? ActualizadoPor { get; set; }
    public DateTime ActualizadoEn { get; set; }
}

/// <summary>Prefacturas y configuración del portal de ventas. Misma base física que <c>PsaWebPlataforma</c> (igual que Reportes y Conciliación SRI).</summary>
public class VentasDbContext(DbContextOptions<VentasDbContext> options) : DbContext(options)
{
    public DbSet<PrefacturaEntidad> Prefacturas => Set<PrefacturaEntidad>();
    public DbSet<PrefacturaLineaEntidad> PrefacturaLineas => Set<PrefacturaLineaEntidad>();
    public DbSet<ConfiguracionVentasEntidad> ConfiguracionesVentas => Set<ConfiguracionVentasEntidad>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        var p = builder.Entity<PrefacturaEntidad>();
        p.ToTable("Prefacturas");
        p.Property(x => x.Ruc).HasMaxLength(20);
        p.Property(x => x.EmpresaNombre).HasMaxLength(200);
        p.Property(x => x.ClienteId).HasMaxLength(60);
        p.Property(x => x.ClienteNombre).HasMaxLength(200);
        p.Property(x => x.ClienteContacto).HasMaxLength(120);
        p.Property(x => x.ClienteTelefono).HasMaxLength(60);
        p.Property(x => x.ClienteEmail).HasMaxLength(200);
        p.Property(x => x.Terminos).HasMaxLength(40);
        p.Property(x => x.Vendedor).HasMaxLength(80);
        p.Property(x => x.Etiqueta).HasMaxLength(120);
        p.Property(x => x.OrdenCliente).HasMaxLength(60);
        p.Property(x => x.DireccionEnvio).HasMaxLength(400);
        p.Property(x => x.NotaCliente).HasMaxLength(1000);
        p.Property(x => x.NotaInterna).HasMaxLength(1000);
        p.Property(x => x.CodigoImpuesto).HasMaxLength(30);
        p.Property(x => x.PorcentajeIva).HasPrecision(9, 4);
        p.Property(x => x.Subtotal).HasPrecision(18, 2);
        p.Property(x => x.Iva).HasPrecision(18, 2);
        p.Property(x => x.Total).HasPrecision(18, 2);
        p.Property(x => x.CreadaPor).HasMaxLength(100);
        p.Property(x => x.CorreoDestinatarios).HasMaxLength(500);
        p.Property(x => x.CorreoError).HasMaxLength(1000);
        p.Property(x => x.FacturaSage).HasMaxLength(40);
        p.Property(x => x.FacturadaPor).HasMaxLength(100);
        p.Property(x => x.FechaEmision).HasColumnType("date");
        p.Property(x => x.ValidaHasta).HasColumnType("date");
        p.HasIndex(x => new { x.Ruc, x.Numero }).IsUnique();
        p.HasIndex(x => new { x.Ruc, x.CreadaEn });
        p.HasMany(x => x.Lineas).WithOne().HasForeignKey(l => l.PrefacturaId).OnDelete(DeleteBehavior.Cascade);

        var l = builder.Entity<PrefacturaLineaEntidad>();
        l.ToTable("PrefacturaLineas");
        l.Property(x => x.ItemId).HasMaxLength(60);
        l.Property(x => x.Descripcion).HasMaxLength(500);
        l.Property(x => x.UnidadMedida).HasMaxLength(20);
        l.Property(x => x.Cantidad).HasPrecision(18, 4);
        l.Property(x => x.PrecioLista).HasPrecision(18, 6);
        l.Property(x => x.PrecioUnitario).HasPrecision(18, 6);
        l.Property(x => x.Monto).HasPrecision(18, 2);
        l.Property(x => x.ExistenciaAlEmitir).HasPrecision(18, 4);

        var c = builder.Entity<ConfiguracionVentasEntidad>();
        c.ToTable("ConfiguracionesVentas");
        c.HasKey(x => x.Ruc);
        c.Property(x => x.Ruc).HasMaxLength(20);
        c.Property(x => x.CorreoContabilidad).HasMaxLength(200);
        c.Property(x => x.CorreoAdicional).HasMaxLength(200);
        c.Property(x => x.CodigoImpuesto).HasMaxLength(30);
        c.Property(x => x.PorcentajeIva).HasPrecision(9, 4);
        c.Property(x => x.ActualizadoPor).HasMaxLength(100);
    }
}

/// <summary>Fábrica para <c>dotnet ef</c> (design-time) — misma cadena que <c>PsaWebPlataforma</c>.</summary>
public sealed class VentasDbContextFactory : IDesignTimeDbContextFactory<VentasDbContext>
{
    public VentasDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("PSAWEB_PLATAFORMA_CS")
                 ?? @"Server=.\SQLEXPRESS;Database=PsaWebPlataforma;Trusted_Connection=True;TrustServerCertificate=True";
        return new VentasDbContext(new DbContextOptionsBuilder<VentasDbContext>().UseSqlServer(cs).Options);
    }
}
