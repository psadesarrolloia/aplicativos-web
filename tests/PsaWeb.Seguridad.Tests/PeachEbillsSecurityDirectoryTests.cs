using Microsoft.EntityFrameworkCore;
using PsaWeb.PeachEbills.Data;
using PsaWeb.Seguridad;

namespace PsaWeb.Seguridad.Tests;

public class PeachEbillsSecurityDirectoryTests
{
    private const string LocalConnectionString =
        @"Server=.\SQLEXPRESS;Database=PeachEBills;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15";

    // Usuario real de la base restaurada, con roles en varias empresas.
    private const string Usuario = "lparedes";
    private const string RucConLote = "1791300165001"; // lparedes tiene mkTwhBatch acá

    private sealed class Factory : IDbContextFactory<PeachEbillsContext>
    {
        private readonly DbContextOptions<PeachEbillsContext> _o =
            new DbContextOptionsBuilder<PeachEbillsContext>().UseSqlServer(LocalConnectionString).Options;
        public PeachEbillsContext CreateDbContext() => new(_o);
    }

    private static bool DbDisponible()
    {
        try { using var c = new Factory().CreateDbContext(); return c.Database.CanConnect(); }
        catch { return false; }
    }

    private static ISecurityDirectory Construir() => new PeachEbillsSecurityDirectory(new Factory());

    [Theory]
    [InlineData("PAREDESSOLUCION\\jperez", "jperez")]
    [InlineData("jperez@paredes.local", "jperez")]
    [InlineData("jperez", "jperez")]
    [InlineData("  DOM\\ Ana ", "Ana")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void NormalizarUsuario_quita_dominio_y_upn(string? entrada, string esperado)
        => Assert.Equal(esperado, PeachEbillsSecurityDirectory.NormalizarUsuario(entrada));

    [SkippableFact]
    public async Task EmpresasDelUsuario_lista_y_ordena_y_acepta_login_con_dominio()
    {
        Skip.IfNot(DbDisponible(), "PeachEBills local no disponible.");
        var dir = Construir();

        var empresas = await dir.EmpresasDelUsuarioAsync(Usuario);
        Assert.NotEmpty(empresas);
        Assert.Contains(empresas, e => e.Ruc == RucConLote);
        Assert.All(empresas, e => Assert.False(string.IsNullOrWhiteSpace(e.Nombre)));
        // ordenadas por Orden y luego nombre
        var ordenadas = empresas.OrderBy(e => e.Orden)
            .ThenBy(e => e.Nombre, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(ordenadas, empresas);

        var conDominio = await dir.EmpresasDelUsuarioAsync($"PAREDESSOLUCION\\{Usuario}");
        Assert.Equal(empresas.Select(e => e.Ruc), conDominio.Select(e => e.Ruc));
    }

    [SkippableFact]
    public async Task Usuario_desconocido_no_tiene_empresas_ni_permisos()
    {
        Skip.IfNot(DbDisponible(), "PeachEBills local no disponible.");
        var dir = Construir();

        Assert.Empty(await dir.EmpresasDelUsuarioAsync("no-existe-xyz"));
        Assert.Empty(await dir.PermisosAsync("no-existe-xyz", RucConLote));
    }

    [SkippableFact]
    public async Task Permisos_resuelve_los_codigos_por_empresa()
    {
        Skip.IfNot(DbDisponible(), "PeachEBills local no disponible.");
        var dir = Construir();

        var permisos = await dir.PermisosAsync(Usuario, RucConLote);
        Assert.Contains(Permisos.VerRetenciones, permisos);       // qupurchtwh
        Assert.Contains(Permisos.HacerRetencionesLote, permisos); // mkTwhBatch

        Assert.True(await dir.TienePermisoAsync(Usuario, RucConLote, Permisos.HacerRetencionesLote));
        Assert.False(await dir.TienePermisoAsync(Usuario, RucConLote, "codigo-inventado"));

        var ctx = await dir.ContextoAsync(Usuario, RucConLote);
        Assert.True(ctx.Puede(Permisos.VerRetenciones));
    }

    [SkippableFact]
    public async Task AppCatalogo_habilita_Retenciones_para_quien_puede_verlas()
    {
        Skip.IfNot(DbDisponible(), "PeachEBills local no disponible.");
        var dir = Construir();

        var ctx = await dir.ContextoAsync(Usuario, RucConLote);
        var apps = AppCatalogo.Habilitadas(ctx).Select(a => a.Id).ToList();

        Assert.Contains("retenciones", apps);
        Assert.Contains("cierre-de-caja", apps); // sin código propio: siempre visible
    }
}
