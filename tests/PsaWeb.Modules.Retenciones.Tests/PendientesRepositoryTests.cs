using Microsoft.EntityFrameworkCore;
using PsaWeb.Modules.Retenciones.Data;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.Retenciones.Tests;

/// <summary>
/// Pruebas de integración contra la copia local de <c>PeachEBills</c> en
/// <c>.\SQLEXPRESS</c>. Se omiten si la base no está disponible.
/// </summary>
public class PendientesRepositoryTests
{
    private const string LocalConnectionString =
        @"Server=.\SQLEXPRESS;Database=PeachEBills;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15";

    private sealed class Factory : IDbContextFactory<PeachEbillsContext>
    {
        private readonly DbContextOptions<PeachEbillsContext> _options =
            new DbContextOptionsBuilder<PeachEbillsContext>().UseSqlServer(LocalConnectionString).Options;

        public PeachEbillsContext CreateDbContext() => new(_options);
    }

    private static bool DbDisponible()
    {
        try
        {
            using var ctx = new Factory().CreateDbContext();
            return ctx.Database.CanConnect();
        }
        catch { return false; }
    }

    private static PendientesRepository Repo() => new(new Factory());

    [SkippableFact]
    public async Task Lista_empresas_activas_y_respeta_la_omision()
    {
        Skip.IfNot(DbDisponible(), "PeachEBills local no disponible.");

        var repo = Repo();

        var todas = await repo.EmpresasActivasAsync(Array.Empty<string>(), ambienteForzado: null);
        Assert.NotEmpty(todas);
        Assert.All(todas, e => Assert.False(string.IsNullOrWhiteSpace(e.Ruc)));
        Assert.All(todas, e => Assert.True(e.Ambiente is 1 or 2));

        var unRuc = todas[0].Ruc;
        var sinEsa = await repo.EmpresasActivasAsync(new[] { unRuc }, ambienteForzado: null);
        Assert.DoesNotContain(sinEsa, e => e.Ruc == unRuc);
    }

    [SkippableFact]
    public async Task AmbienteForzado_gana_sobre_el_de_la_empresa()
    {
        Skip.IfNot(DbDisponible(), "PeachEBills local no disponible.");

        var empresas = await Repo().EmpresasActivasAsync(Array.Empty<string>(), ambienteForzado: 1);

        Assert.All(empresas, e => Assert.Equal(1, e.Ambiente));
    }

    [SkippableFact]
    public async Task Pendientes_no_incluye_los_ya_generados()
    {
        Skip.IfNot(DbDisponible(), "PeachEBills local no disponible.");

        var repo = Repo();
        var empresas = await repo.EmpresasActivasAsync(Array.Empty<string>(), ambienteForzado: 2);
        var empresa = empresas.First();

        var pendientes = await repo.PendientesAsync(empresa.Ruc, 2);

        // No debe reventar y no debe traer ninguno que ya esté en TaxWithHoldings.
        await using var db = new Factory().CreateDbContext();
        var yaGenerados = db.TaxWithHoldings
            .Where(t => t.TransmitterRuc == empresa.Ruc && t.Ambient == 2)
            .Select(t => t.PostOrderPeach)
            .ToHashSet();

        Assert.All(pendientes, p => Assert.DoesNotContain(p, yaGenerados));
    }
}
