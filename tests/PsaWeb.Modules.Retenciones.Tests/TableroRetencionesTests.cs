using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PsaWeb.Modules.Retenciones;
using PsaWeb.Modules.Retenciones.Data;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.Retenciones.Tests;

/// <summary>
/// Lecturas del tablero contra el <c>PeachEBills</c> local. Solo consultas: no
/// emite ni escribe nada.
/// </summary>
public class TableroRetencionesTests
{
    private const string LocalConnectionString =
        @"Server=.\SQLEXPRESS;Database=PeachEBills;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15";

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

    private static TableroRetenciones Construir()
    {
        var f = new Factory();
        return new TableroRetenciones(f, new PendientesRepository(f), Options.Create(new RetencionesOptions()));
    }

    [SkippableFact]
    public async Task Panorama_lista_empresas_activas_con_conteo_de_pendientes()
    {
        Skip.IfNot(DbDisponible(), "PeachEBills local no disponible.");

        var filas = await Construir().PanoramaAsync();

        Assert.NotEmpty(filas);
        Assert.All(filas, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Ruc));
            Assert.True(f.Pendientes >= 0);
            Assert.Contains(f.Ambiente, new short[] { 1, 2 });
        });
    }

    [SkippableFact]
    public async Task Recientes_respeta_el_tope_y_ordena_por_fecha_desc()
    {
        Skip.IfNot(DbDisponible(), "PeachEBills local no disponible.");

        var tablero = Construir();
        var recientes = await tablero.RecientesAsync(top: 10);

        Assert.True(recientes.Count <= 10);
        for (var i = 1; i < recientes.Count; i++)
        {
            Assert.True(recientes[i - 1].Fecha >= recientes[i].Fecha);
        }

        // El desempate por THId hace el orden determinista: dos lecturas seguidas
        // deben devolver exactamente la misma secuencia de números.
        var otra = await tablero.RecientesAsync(top: 10);
        Assert.Equal(
            recientes.Select(r => r.Numero).ToArray(),
            otra.Select(r => r.Numero).ToArray());
    }
}
