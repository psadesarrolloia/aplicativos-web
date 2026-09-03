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

    [SkippableFact]
    public async Task Recientes_acota_por_rango_de_fechas_inclusive()
    {
        Skip.IfNot(DbDisponible(), "PeachEBills local no disponible.");

        var tablero = Construir();

        // Tomo la fecha de la retención más nueva y pido solo ese día.
        var ultima = (await tablero.RecientesAsync(top: 1)).FirstOrDefault();
        Skip.If(ultima is null, "No hay retenciones guardadas.");
        var dia = ultima!.Fecha.Date;

        var delDia = await tablero.RecientesAsync(desde: dia, hasta: dia, top: 500);

        Assert.NotEmpty(delDia);
        Assert.All(delDia, r => Assert.Equal(dia, r.Fecha.Date));

        // Un rango en el futuro no trae nada.
        var futuro = await tablero.RecientesAsync(desde: dia.AddYears(50), hasta: dia.AddYears(50), top: 10);
        Assert.Empty(futuro);
    }

    [SkippableFact]
    public async Task Recientes_resuelve_el_nombre_del_proveedor_desde_Persons()
    {
        Skip.IfNot(DbDisponible(), "PeachEBills local no disponible.");

        var recientes = await Construir().RecientesAsync(top: 100);
        Skip.If(recientes.Count == 0, "No hay retenciones guardadas.");

        // La base tiene la tabla Persons poblada: al menos alguna debe resolver.
        Assert.Contains(recientes, r => !string.IsNullOrWhiteSpace(r.ProveedorNombre));
        // Y cuando resuelve, el id sigue disponible para el tooltip.
        Assert.All(recientes.Where(r => r.ProveedorNombre is not null),
            r => Assert.False(string.IsNullOrWhiteSpace(r.ProveedorId)));
    }

    [SkippableFact]
    public async Task PendientesDetalle_cap_y_total()
    {
        Skip.IfNot(DbDisponible(), "PeachEBills local no disponible.");

        var tablero = Construir();
        var conPendientes = (await tablero.PanoramaAsync()).FirstOrDefault(f => f.Pendientes > 0);
        Skip.If(conPendientes is null, "Ninguna empresa tiene pendientes.");

        var (filas, total) = await tablero.PendientesDetalleAsync(
            conPendientes!.Ruc, conPendientes.Nombre, conPendientes.Ambiente, top: 5);

        Assert.Equal(conPendientes.Pendientes, total);
        Assert.True(filas.Count <= 5);
        Assert.True(filas.Count <= total);
        Assert.All(filas, p => Assert.False(string.IsNullOrWhiteSpace(p.Referencia)));
    }
}
