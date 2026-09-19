using Microsoft.EntityFrameworkCore;
using PsaWeb.Conciliacion.Data;

namespace PsaWeb.Conciliacion.Tests;

/// <summary>Pruebas contra la base local <c>PsaWebPlataforma</c> (se saltean si no está disponible).</summary>
public class RepositorioComprobantesSriTests : IAsyncLifetime
{
    private const string LocalConnectionString =
        @"Server=.\SQLEXPRESS;Database=PsaWebPlataforma;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15";

    private const string Ruc = "1799999999002"; // distinto del usado en LectorReporteComprobantesSriTests, no comparten datos.

    private const string Encabezado =
        "RUC_EMISOR\tRAZON_SOCIAL_EMISOR\tTIPO_COMPROBANTE\tSERIE_COMPROBANTE\tCLAVE_ACCESO\t" +
        "FECHA_AUTORIZACION\tFECHA_EMISION\tIDENTIFICACION_RECEPTOR\tVALOR_SIN_IMPUESTOS\tIVA\t" +
        "IMPORTE_TOTAL\tNUMERO_DOCUMENTO_MODIFICADO";

    private const string Fila1 =
        "1791111111001\tPROVEEDOR DEMO UNO S.A.\tFactura\t001-001-000000001\t" +
        "0109202601179111111100120010010000000011234567811\t01/09/2026 09:00:00\t01/09/2026\t" +
        Ruc + "\t100\t12\t112\t";

    private const string Fila2 =
        "1791111111001\tPROVEEDOR DEMO UNO S.A.\tFactura\t001-001-000000002\t" +
        "0209202601179111111100120010010000000021234567812\t02/09/2026 10:00:00\t02/09/2026\t" +
        Ruc + "\t50\t6\t56\t";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (!DbDisponible())
        {
            return;
        }

        await using var db = Db();
        var propios = await db.ComprobantesSriDescargados.Where(c => c.Ruc == Ruc).ToListAsync();
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

    private static string Reporte(params string[] filas) => string.Join('\n', new[] { Encabezado }.Concat(filas));

    [SkippableFact]
    public async Task Guarda_filas_nuevas_y_las_deja_disponibles_por_ruc()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using var db = Db();
        var repo = new RepositorioComprobantesSri(db);

        var resultado = await repo.GuardarReporteAsync(Ruc, Reporte(Fila1, Fila2), "usuario-de-prueba");

        Assert.Equal(2, resultado.Total);
        Assert.Equal(2, resultado.Nuevos);
        Assert.Equal(0, resultado.YaExistian);

        var guardados = await db.ComprobantesSriDescargados.Where(c => c.Ruc == Ruc).ToListAsync();
        Assert.Equal(2, guardados.Count);
    }

    [SkippableFact]
    public async Task Subir_el_mismo_reporte_dos_veces_no_duplica()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using var db = Db();
        var repo = new RepositorioComprobantesSri(db);

        await repo.GuardarReporteAsync(Ruc, Reporte(Fila1, Fila2), "usuario-de-prueba");
        var segunda = await repo.GuardarReporteAsync(Ruc, Reporte(Fila1, Fila2), "usuario-de-prueba");

        Assert.Equal(0, segunda.Nuevos);
        Assert.Equal(2, segunda.YaExistian);

        var guardados = await db.ComprobantesSriDescargados.Where(c => c.Ruc == Ruc).ToListAsync();
        Assert.Equal(2, guardados.Count); // sigue habiendo solo 2, no 4.
    }

    [SkippableFact]
    public async Task No_pisa_los_datos_de_un_comprobante_ya_guardado()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using var db = Db();
        var repo = new RepositorioComprobantesSri(db);

        await repo.GuardarReporteAsync(Ruc, Reporte(Fila1), "primer-usuario");
        // Mismo comprobante, pero si el archivo trajera un monto distinto (no debería pasar en la
        // realidad — un comprobante autorizado no cambia — pero el repositorio no debe pisarlo igual).
        var filaConOtroMonto = Fila1.Replace("\t100\t12\t112\t", "\t999\t0\t999\t");
        await repo.GuardarReporteAsync(Ruc, Reporte(filaConOtroMonto), "segundo-usuario");

        var guardado = await db.ComprobantesSriDescargados.SingleAsync(c => c.Ruc == Ruc);
        Assert.Equal(100m, guardado.Subtotal);
        Assert.Equal("primer-usuario", guardado.SubidoPor);
    }

    [SkippableFact]
    public async Task Aceptar_diferencia_guarda_quien_cuando_y_el_comentario()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using var db = Db();
        var repo = new RepositorioComprobantesSri(db);
        await repo.GuardarReporteAsync(Ruc, Reporte(Fila1), "usuario-de-prueba");
        var id = (await db.ComprobantesSriDescargados.SingleAsync(c => c.Ruc == Ruc)).Id;

        await repo.AceptarDiferenciaAsync(id, "lparedes", "Corresponde a ICE");

        // Contexto nuevo: ExecuteUpdateAsync no pasa por el change tracker, así
        // que reconsultar con el mismo "db" devolvería la instancia ya
        // trackeada (con los valores viejos) en vez de releer la fila.
        await using var dbVerificacion = Db();
        var actualizado = await dbVerificacion.ComprobantesSriDescargados.SingleAsync(c => c.Id == id);
        Assert.True(actualizado.DiferenciaAceptada);
        Assert.Equal("lparedes", actualizado.DiferenciaAceptadaPor);
        Assert.Equal("Corresponde a ICE", actualizado.ComentarioAceptacion);
        Assert.NotNull(actualizado.DiferenciaAceptadaUtc);
    }

    [SkippableFact]
    public async Task Quitar_aceptacion_deshace_la_marca()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using var db = Db();
        var repo = new RepositorioComprobantesSri(db);
        await repo.GuardarReporteAsync(Ruc, Reporte(Fila1), "usuario-de-prueba");
        var id = (await db.ComprobantesSriDescargados.SingleAsync(c => c.Ruc == Ruc)).Id;
        await repo.AceptarDiferenciaAsync(id, "lparedes", null);

        await repo.QuitarAceptacionAsync(id);

        await using var dbVerificacion = Db();
        var actualizado = await dbVerificacion.ComprobantesSriDescargados.SingleAsync(c => c.Id == id);
        Assert.False(actualizado.DiferenciaAceptada);
        Assert.Null(actualizado.DiferenciaAceptadaPor);
        Assert.Null(actualizado.ComentarioAceptacion);
    }

    [SkippableFact]
    public async Task Aceptar_diferencia_de_un_id_inexistente_lanza()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using var db = Db();
        var repo = new RepositorioComprobantesSri(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.AceptarDiferenciaAsync(-1, "lparedes", null));
    }
}
