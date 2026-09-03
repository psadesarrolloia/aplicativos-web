using System.Data.Odbc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PsaWeb.Comprobantes.Retenciones;
using PsaWeb.Datil;
using PsaWeb.Datil.Model;
using PsaWeb.Modules.Retenciones;
using PsaWeb.Modules.Retenciones.Data;
using PsaWeb.PeachEbills;
using PsaWeb.PeachEbills.Data;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.Retenciones.Tests;

/// <summary>
/// Prueba de cableado de <see cref="ProcesadorRetenciones"/> contra PeachEBills
/// local y Datil en <c>DryRun</c>, con una fábrica de conexiones Sage que falla
/// (PREDATOR no llega a los servidores Sage multi-empresa). Verifica que el
/// orquestador degrada con gracia y arma el <see cref="ResumenCorrida"/>.
/// </summary>
public class ProcesadorRetencionesTests
{
    private const string LocalConnectionString =
        @"Server=.\SQLEXPRESS;Database=PeachEBills;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3";

    private sealed class Factory : IDbContextFactory<PeachEbillsContext>
    {
        private readonly DbContextOptions<PeachEbillsContext> _o =
            new DbContextOptionsBuilder<PeachEbillsContext>().UseSqlServer(LocalConnectionString).Options;
        public PeachEbillsContext CreateDbContext() => new(_o);
    }

    private sealed class SageQueFalla : ISageConnectionFactory
    {
        public OdbcConnection CreateConnection() => throw new NotSupportedException();
        public OdbcConnection CreateConnection(string connectionString) => new("Driver={Driver Inexistente PSA};");
    }

    private sealed class EstablecimientoFake : IEstablecimientoLookup
    {
        public Task<EstablecimientoInfo?> BuscarAsync(string ruc, string codigo, string puntoEmision, CancellationToken ct = default)
            => Task.FromResult<EstablecimientoInfo?>(new EstablecimientoInfo(1, codigo, puntoEmision, "s/d"));
    }

    private sealed class InfoAdicionalFake : IInfoAdicionalLookup
    {
        public Task<IReadOnlyDictionary<string, string>?> ObtenerAsync(string ruc, string codDoc, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>?>(null);
    }

    private sealed class DatilDryRunFake : IDatilClient
    {
        public Task<DatilEmisionResult> EmitirRetencionAsync(Retencion retencion, DatilCredentials credenciales, CancellationToken ct = default)
            => Task.FromResult(DatilEmisionResult.DryRun("{}"));
        public Task<string?> ConsultarEstadoAsync(string id, DatilCredentials credenciales, CancellationToken ct = default)
            => Task.FromResult<string?>("dry-run");
    }

    private static bool DbDisponible()
    {
        try { using var c = new Factory().CreateDbContext(); return c.Database.CanConnect(); }
        catch { return false; }
    }

    private static ProcesadorRetenciones Construir()
    {
        var f = new Factory();
        return new ProcesadorRetenciones(
            new PendientesRepository(f),
            new EmpresaLookup(f),
            new PeachConnStringResolver(f),
            new SageQueFalla(),
            new RetencionBuilder(new EstablecimientoFake(), new InfoAdicionalFake()),
            new DatilDryRunFake(),
            new RepositorioRetenciones(f),
            Options.Create(new RetencionesOptions()),
            Options.Create(new DatilOptions { DryRun = true }),
            NullLogger<ProcesadorRetenciones>.Instance);
    }

    [SkippableFact]
    public async Task Corre_todas_las_empresas_sin_reventar_y_arma_el_resumen()
    {
        Skip.IfNot(DbDisponible(), "PeachEBills local no disponible.");

        var resumen = await Construir().ProcesarTodasAsync(usuario: "test");

        Assert.True(resumen.DryRun);
        Assert.NotEmpty(resumen.Empresas);
        Assert.Equal(0, resumen.TotalEmitidas); // Sage no accesible => nada emitido

        // Cada empresa: o no tiene pendientes, o reporta el fallo de Sage / configuración.
        Assert.All(resumen.Empresas, e =>
            Assert.True(
                e.Pendientes == 0 ||
                e.Mensajes.Any(m => m.Contains("Sage 50") || m.Contains("Configuración")),
                $"RUC {e.Ruc}: {string.Join(" ; ", e.Mensajes)}"));
    }
}
