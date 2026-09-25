using Microsoft.EntityFrameworkCore;
using PsaWeb.SageBridge.Cola;
using PsaWeb.SageBridge.Cola.Data;
using PsaWeb.SageBridge.Contratos;

namespace PsaWeb.SageBridge.Tests;

/// <summary>Pruebas contra la base local <c>PsaWebPlataforma</c> (se saltean si no está disponible).</summary>
public class ColaSageTests : IAsyncLifetime
{
    private const string LocalConnectionString =
        @"Server=.\SQLEXPRESS;Database=PsaWebPlataforma;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15";

    private const string Ruc = "1799999999004"; // exclusivo de este archivo de test

    private sealed class Fabrica : IDbContextFactory<SageBridgeDbContext>
    {
        public SageBridgeDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<SageBridgeDbContext>().UseSqlServer(LocalConnectionString).Options);
    }

    private static readonly Fabrica Db = new();
    private static bool? _disponible;

    private static bool DbDisponible()
    {
        if (_disponible is not null) return _disponible.Value;
        try
        {
            using var db = Db.CreateDbContext();
            if (!db.Database.CanConnect()) return (_disponible = false).Value;
            db.Database.Migrate(); // el Host lo hace al arrancar; acá para que el test no dependa de eso
            return (_disponible = true).Value;
        }
        catch { return (_disponible = false).Value; }
    }

    public async Task InitializeAsync() => await Limpiar();
    public async Task DisposeAsync() => await Limpiar();

    private static async Task Limpiar()
    {
        if (!DbDisponible()) return;
        await using var db = Db.CreateDbContext();
        await db.Trabajos.Where(t => t.Ruc == Ruc).ExecuteDeleteAsync();
        await db.Empresas.Where(e => e.Ruc == Ruc).ExecuteDeleteAsync();
    }

    [SkippableFact]
    public async Task Encolar_dos_veces_la_misma_clave_devuelve_el_mismo_trabajo()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        var cola = new ColaSage(Db);

        var a = await cola.EncolarAsync(Ruc, TiposTrabajo.ProbarEmpresa, null, "clave-1", "tester");
        var b = await cola.EncolarAsync(Ruc, TiposTrabajo.ProbarEmpresa, "{\"x\":1}", "clave-1", "otro");

        Assert.True(a.Nuevo);
        Assert.False(b.Nuevo);
        Assert.Equal(a.Trabajo.Id, b.Trabajo.Id);
        Assert.Equal(EstadosTrabajo.EnCola, b.Trabajo.Estado);
        Assert.Null(b.Trabajo.PayloadJson); // conserva el original
    }

    [SkippableFact]
    public async Task Encolar_rechaza_tipos_desconocidos_y_claves_vacias()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        var cola = new ColaSage(Db);

        await Assert.ThrowsAsync<ArgumentException>(() => cola.EncolarAsync(Ruc, "BorrarTodo", null, "k", "tester"));
        await Assert.ThrowsAsync<ArgumentException>(() => cola.EncolarAsync(Ruc, TiposTrabajo.ProbarEmpresa, null, " ", "tester"));
    }

    [SkippableFact]
    public async Task Cancelar_solo_en_cola_y_reintentar_solo_terminados_con_error()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        var cola = new ColaSage(Db);
        var t = (await cola.EncolarAsync(Ruc, TiposTrabajo.ProbarEmpresa, null, "clave-2", "tester")).Trabajo;

        Assert.False(await cola.ReintentarAsync(t.Id, "tester")); // en cola: no hay nada que reintentar
        Assert.True(await cola.CancelarAsync(t.Id, "tester"));
        Assert.False(await cola.CancelarAsync(t.Id, "tester")); // ya cancelado

        var cancelado = await cola.ObtenerAsync(t.Id);
        Assert.Equal(EstadosTrabajo.Cancelado, cancelado!.Estado);
        Assert.NotNull(cancelado.TerminadoUtc);

        Assert.True(await cola.ReintentarAsync(t.Id, "tester"));
        var reintentado = await cola.ObtenerAsync(t.Id);
        Assert.Equal(EstadosTrabajo.EnCola, reintentado!.Estado);
        Assert.Equal(0, reintentado.Intentos);
        Assert.Null(reintentado.TerminadoUtc);
        Assert.Null(reintentado.Error);
    }

    [SkippableFact]
    public async Task Guardar_empresa_valida_y_normaliza_la_ventana()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        var cola = new ColaSage(Db);

        await Assert.ThrowsAsync<ArgumentException>(() => cola.GuardarEmpresaAsync(Ruc, true, "22-06", null, "tester"));

        var e = await cola.GuardarEmpresaAsync(Ruc, true, " 22:00 - 06:00 ", "  prueba  ", "tester");
        Assert.Equal("22:00-06:00", e.Ventana);
        Assert.Equal("prueba", e.Nota);

        var sinVentana = await cola.GuardarEmpresaAsync(Ruc, false, "", null, "tester");
        Assert.Null(sinVentana.Ventana);
        Assert.False(sinVentana.Habilitada);
        Assert.Single(await cola.EmpresasAsync(), x => x.Ruc == Ruc);
    }

    [SkippableFact]
    public async Task Resumen_cuenta_por_empresa_y_estado()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        var cola = new ColaSage(Db);
        await cola.EncolarAsync(Ruc, TiposTrabajo.ProbarEmpresa, null, "r1", "tester");
        await cola.EncolarAsync(Ruc, TiposTrabajo.ProbarEmpresa, null, "r2", "tester");
        var t3 = (await cola.EncolarAsync(Ruc, TiposTrabajo.ProbarEmpresa, null, "r3", "tester")).Trabajo;
        await cola.CancelarAsync(t3.Id, "tester");

        var resumen = (await cola.ResumenAsync()).Where(r => r.Ruc == Ruc).ToList();
        Assert.Equal(2, resumen.Single(r => r.Estado == EstadosTrabajo.EnCola).Cantidad);
        Assert.Equal(1, resumen.Single(r => r.Estado == EstadosTrabajo.Cancelado).Cantidad);
    }
}
