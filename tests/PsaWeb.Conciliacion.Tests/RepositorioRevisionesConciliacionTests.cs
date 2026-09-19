using Microsoft.EntityFrameworkCore;
using PsaWeb.Conciliacion.Data;

namespace PsaWeb.Conciliacion.Tests;

/// <summary>Pruebas contra la base local <c>PsaWebPlataforma</c> (se saltean si no está disponible).</summary>
public class RepositorioRevisionesConciliacionTests : IAsyncLifetime
{
    private const string LocalConnectionString =
        @"Server=.\SQLEXPRESS;Database=PsaWebPlataforma;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15";

    private const string Ruc = "1799999999004"; // propio de esta clase: no comparte datos con las otras.
    private const string Clave = "C:0109202601179111111100120010010000000011234567811";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (!DbDisponible())
        {
            return;
        }

        await using var db = Db();
        await db.RevisionesConciliacion.Where(r => r.Ruc == Ruc).ExecuteDeleteAsync();
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

    [SkippableFact]
    public async Task Registrar_guarda_quien_cuando_el_comentario_y_la_huella()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using var db = Db();
        var repo = new RepositorioRevisionesConciliacion(db);

        await repo.RegistrarAsync(Ruc, Clave, "huella-1", "  Corresponde a ICE  ", "lparedes");

        await using var verificacion = Db();
        var guardadas = await new RepositorioRevisionesConciliacion(verificacion).ListarAsync(Ruc);
        var r = Assert.Single(guardadas).Value;
        Assert.Equal(Clave, r.Clave);
        Assert.Equal("huella-1", r.Huella);
        Assert.Equal("Corresponde a ICE", r.Comentario); // recortado
        Assert.Equal("lparedes", r.RevisadaPor);
    }

    [SkippableFact]
    public async Task Registrar_dos_veces_la_misma_fila_reemplaza_la_revision()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using var db = Db();
        var repo = new RepositorioRevisionesConciliacion(db);

        await repo.RegistrarAsync(Ruc, Clave, "huella-1", "primera", "ana");
        await repo.RegistrarAsync(Ruc, Clave, "huella-2", "segunda", "luis");

        await using var verificacion = Db();
        var guardadas = await new RepositorioRevisionesConciliacion(verificacion).ListarAsync(Ruc);
        var r = Assert.Single(guardadas).Value;
        Assert.Equal("huella-2", r.Huella);
        Assert.Equal("segunda", r.Comentario);
        Assert.Equal("luis", r.RevisadaPor);
    }

    [SkippableTheory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task El_comentario_es_obligatorio(string comentario)
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using var db = Db();
        var repo = new RepositorioRevisionesConciliacion(db);

        await Assert.ThrowsAsync<ArgumentException>(() => repo.RegistrarAsync(Ruc, Clave, "h", comentario, "ana"));
        Assert.Empty(await repo.ListarAsync(Ruc));
    }

    [SkippableFact]
    public async Task El_comentario_no_puede_pasar_del_maximo()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using var db = Db();
        var repo = new RepositorioRevisionesConciliacion(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repo.RegistrarAsync(Ruc, Clave, "h", new string('x', ClaveRevision.MaxComentario + 1), "ana"));
    }

    [SkippableFact]
    public async Task Quitar_deshace_la_revision_y_no_falla_si_no_existia()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using var db = Db();
        var repo = new RepositorioRevisionesConciliacion(db);
        await repo.RegistrarAsync(Ruc, Clave, "h", "ok", "ana");

        await repo.QuitarAsync(Ruc, Clave);
        await repo.QuitarAsync(Ruc, Clave); // segunda vez: nada que quitar

        Assert.Empty(await repo.ListarAsync(Ruc));
    }

    [SkippableFact]
    public async Task Las_revisiones_de_una_empresa_no_se_ven_en_otra()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using var db = Db();
        var repo = new RepositorioRevisionesConciliacion(db);
        await repo.RegistrarAsync(Ruc, Clave, "h", "ok", "ana");

        Assert.Empty(await repo.ListarAsync("1799999999005"));
    }
}
