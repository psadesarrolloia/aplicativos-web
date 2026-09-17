using Microsoft.EntityFrameworkCore;
using PsaWeb.Conciliacion.Data;

namespace PsaWeb.Conciliacion.Tests;

/// <summary>Pruebas contra la base local <c>PsaWebPlataforma</c> (se saltean si no está disponible).</summary>
public class LectorComprobantesSriTests : IAsyncLifetime
{
    private const string LocalConnectionString =
        @"Server=.\SQLEXPRESS;Database=PsaWebPlataforma;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15";

    private const string Ruc = "1799999999003"; // distinto de los otros archivos de test, no comparten datos.
    private const string OtroRuc = "1788888888003";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (!DbDisponible())
        {
            return;
        }

        await using var db = Db();
        var propios = await db.ComprobantesSriDescargados
            .Where(c => c.Ruc == Ruc || c.Ruc == OtroRuc)
            .ToListAsync();
        db.ComprobantesSriDescargados.RemoveRange(propios);
        await db.SaveChangesAsync();
    }

    private static bool DbDisponible()
    {
        try
        {
            using var db = Db();
            return db.Database.CanConnect();
        }
        catch { return false; }
    }

    private static ConciliacionDbContext Db() =>
        new(new DbContextOptionsBuilder<ConciliacionDbContext>().UseSqlServer(LocalConnectionString).Options);

    private static ComprobanteSriDescargado Comprobante(string ruc, string clave, DateOnly fechaEmision) => new()
    {
        Ruc = ruc,
        ClaveAcceso = clave,
        RucEmisor = "1791111111001",
        RazonSocialEmisor = "PROVEEDOR DEMO S.A.",
        TipoComprobante = "Factura",
        SerieComprobante = "001-001-000000001",
        FechaAutorizacion = fechaEmision.ToDateTime(TimeOnly.MinValue),
        FechaEmision = fechaEmision,
        IdentificacionReceptor = ruc,
        Subtotal = 100,
        Iva = 12,
        Total = 112,
        SubidoPor = "usuario-de-prueba",
    };

    [SkippableFact]
    public async Task Trae_solo_las_filas_del_ruc_y_el_rango_de_fechas_pedidos()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using (var db = Db())
        {
            db.ComprobantesSriDescargados.AddRange(
                Comprobante(Ruc, "1".PadLeft(49, '0'), new DateOnly(2026, 9, 5)),   // dentro del rango
                Comprobante(Ruc, "2".PadLeft(49, '0'), new DateOnly(2026, 9, 15)),  // dentro del rango
                Comprobante(Ruc, "3".PadLeft(49, '0'), new DateOnly(2026, 8, 31)),  // antes del rango
                Comprobante(Ruc, "4".PadLeft(49, '0'), new DateOnly(2026, 10, 1)),  // después del rango
                Comprobante(OtroRuc, "5".PadLeft(49, '0'), new DateOnly(2026, 9, 10))); // otra empresa
            await db.SaveChangesAsync();
        }

        await using var dbLectura = Db();
        var lector = new LectorComprobantesSri(dbLectura);
        var resultado = await lector.ObtenerAsync(Ruc, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        // Solo 2 filas: ni la de fuera de rango, ni la de OtroRuc (que cae en el rango pero es otra empresa).
        Assert.Equal(2, resultado.Count);
        Assert.Contains(resultado, c => c.FechaEmision == new DateOnly(2026, 9, 5));
        Assert.Contains(resultado, c => c.FechaEmision == new DateOnly(2026, 9, 15));
        Assert.True(resultado[0].FechaEmision <= resultado[1].FechaEmision); // orden cronológico
    }
}
