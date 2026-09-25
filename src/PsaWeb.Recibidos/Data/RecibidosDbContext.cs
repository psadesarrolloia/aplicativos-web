using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PsaWeb.Recibidos.Data;

/// <summary>De dónde llegó el XML.</summary>
public static class OrigenesXml
{
    public const string WsSri = "WsSri";
    public const string Extension = "Extension";
    public const string SubidaManual = "SubidaManual";
}

/// <summary>
/// XML autorizado de un comprobante recibido (§13.1 del plan de la Ola 2): una fila por <see cref="Ruc"/> + <see cref="ClaveAcceso"/>,
/// inmutable, el documento tal como llegó comprimido con gzip. Se conserva 7 años desde la emisión (§13.2).
/// </summary>
public sealed class XmlComprobanteRecibido
{
    public long Id { get; set; }

    /// <summary>RUC de la empresa receptora.</summary>
    [MaxLength(13)] public string Ruc { get; set; } = string.Empty;

    [MaxLength(49)] public string ClaveAcceso { get; set; } = string.Empty;

    /// <summary><c>Factura</c>, <c>NotaCredito</c>, <c>Retencion</c>.</summary>
    [MaxLength(20)] public string TipoComprobante { get; set; } = string.Empty;

    [MaxLength(13)] public string RucEmisor { get; set; } = string.Empty;

    public DateOnly FechaEmision { get; set; }
    public DateTime? FechaAutorizacion { get; set; }

    /// <summary>El archivo tal como llegó (respuesta SOAP, <c>&lt;autorizacion&gt;</c> o comprobante suelto), gzip.</summary>
    public byte[] Xml { get; set; } = [];

    /// <summary>SHA-256 (hex) del contenido sin comprimir.</summary>
    [MaxLength(64)] public string HashSha256 { get; set; } = string.Empty;

    [MaxLength(20)] public string Origen { get; set; } = string.Empty;
    [MaxLength(256)] public string SubidoPor { get; set; } = string.Empty;
    public DateTime FechaCargaUtc { get; set; }
}

/// <summary>Otro XML distinto (hash diferente) para una clave ya guardada: no se pisa, se registra para revisión.</summary>
public sealed class ConflictoXmlRecibido
{
    public long Id { get; set; }
    [MaxLength(13)] public string Ruc { get; set; } = string.Empty;
    [MaxLength(49)] public string ClaveAcceso { get; set; } = string.Empty;
    public byte[] Xml { get; set; } = [];
    [MaxLength(64)] public string HashSha256 { get; set; } = string.Empty;
    [MaxLength(20)] public string Origen { get; set; } = string.Empty;
    [MaxLength(256)] public string SubidoPor { get; set; } = string.Empty;
    public DateTime FechaUtc { get; set; }
}

/// <summary>
/// Intento de descarga por el WS sin resultado (fuera de la ventana de ~15 días del WS, o error): sirve para medir cuántos
/// documentos quedan para la subida manual (§13.5) y para no reintentar sin fin.
/// </summary>
public sealed class DescargaXmlFallida
{
    public long Id { get; set; }
    [MaxLength(13)] public string Ruc { get; set; } = string.Empty;
    [MaxLength(49)] public string ClaveAcceso { get; set; } = string.Empty;

    /// <summary><c>SinXml</c> (el WS respondió sin comprobante) o <c>Error</c>.</summary>
    [MaxLength(20)] public string Motivo { get; set; } = string.Empty;
    [MaxLength(500)] public string? Detalle { get; set; }
    public int Intentos { get; set; }
    public DateTime UltimoIntentoUtc { get; set; }
}

public class RecibidosDbContext(DbContextOptions<RecibidosDbContext> options) : DbContext(options)
{
    public DbSet<XmlComprobanteRecibido> Xmls => Set<XmlComprobanteRecibido>();
    public DbSet<ConflictoXmlRecibido> Conflictos => Set<ConflictoXmlRecibido>();
    public DbSet<DescargaXmlFallida> DescargasFallidas => Set<DescargaXmlFallida>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<XmlComprobanteRecibido>(x =>
        {
            x.ToTable("XmlComprobantesRecibidos");
            x.HasIndex(e => new { e.Ruc, e.ClaveAcceso }).IsUnique();
            x.HasIndex(e => new { e.Ruc, e.FechaEmision });
        });
        builder.Entity<ConflictoXmlRecibido>().ToTable("ConflictosXmlRecibidos");
        builder.Entity<DescargaXmlFallida>(x =>
        {
            x.ToTable("DescargasXmlFallidas");
            x.HasIndex(e => new { e.Ruc, e.ClaveAcceso }).IsUnique();
        });
    }
}

/// <summary>Fábrica para <c>dotnet ef</c> (design-time) — misma cadena que <c>PsaWebPlataforma</c>.</summary>
public sealed class RecibidosDbContextFactory : IDesignTimeDbContextFactory<RecibidosDbContext>
{
    public RecibidosDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("PSAWEB_PLATAFORMA_CS")
                 ?? @"Server=.\SQLEXPRESS;Database=PsaWebPlataforma;Trusted_Connection=True;TrustServerCertificate=True";
        return new RecibidosDbContext(new DbContextOptionsBuilder<RecibidosDbContext>().UseSqlServer(cs).Options);
    }
}
