using Microsoft.EntityFrameworkCore;
using PsaWeb.Modules.ComprobantesElectronicos.Data;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.ComprobantesElectronicos.Tests;

/// <summary>
/// Integración: verifica que las consultas del tablero corren contra la copia
/// local de <c>PeachEBills</c>. Si la base no está, se omite. No exige que haya
/// comprobantes cargados.
/// </summary>
public class TableroComprobantesTests
{
    private const string LocalCs =
        @"Server=.\SQLEXPRESS;Database=PeachEBills;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3";

    private sealed class Factory : IDbContextFactory<PeachEbillsContext>
    {
        private readonly DbContextOptions<PeachEbillsContext> _o =
            new DbContextOptionsBuilder<PeachEbillsContext>().UseSqlServer(LocalCs).Options;
        public PeachEbillsContext CreateDbContext() => new(_o);
    }

    private static bool DbOk()
    {
        try { using var c = new Factory().CreateDbContext(); return c.Database.CanConnect(); }
        catch { return false; }
    }

    [SkippableFact]
    public async Task Recientes_corre_para_los_3_tipos_sin_reventar()
    {
        Skip.IfNot(DbOk(), "PeachEBills local no disponible.");
        var tablero = new TableroComprobantes(new Factory());

        foreach (var codDoc in new[] { "01", "04", "03" })
        {
            var filas = await tablero.RecientesAsync(
                "1791313747001", codDoc,
                DateTime.Today.AddMonths(-6), DateTime.Today, top: 50);
            Assert.NotNull(filas);
            Assert.All(filas, f => Assert.False(string.IsNullOrEmpty(f.Numero)));
        }
    }

    [SkippableFact]
    public async Task Detalle_de_un_id_inexistente_es_null()
    {
        Skip.IfNot(DbOk(), "PeachEBills local no disponible.");
        var tablero = new TableroComprobantes(new Factory());

        Assert.Null(await tablero.DetalleAsync(-999));
    }

    [SkippableFact]
    public async Task Recientes_de_retenciones_lee_TaxWithHoldings_con_total_retenido_y_sin_iva()
    {
        Skip.IfNot(DbOk(), "PeachEBills local no disponible.");
        var tablero = new TableroComprobantes(new Factory());

        // RUC con retenciones emitidas en la copia local (verificado 2026-09-19: 1.069 en jul–sep).
        var filas = await tablero.RecientesAsync(
            "1792051800001", "07", DateTime.Today.AddMonths(-3), DateTime.Today, top: 50);

        Skip.If(filas.Count == 0, "La copia local no trae retenciones recientes para ese RUC.");
        Assert.All(filas, f =>
        {
            Assert.False(string.IsNullOrEmpty(f.Numero));
            Assert.Equal(0, f.Iva);              // las retenciones no llevan IVA
            Assert.True(f.Total >= 0);           // «Total» = total retenido
            Assert.Equal("1792051800001", f.Ruc);
            Assert.False(string.IsNullOrEmpty(f.Empresa));
        });
        Assert.Contains(filas, f => f.Total > 0);
    }

    [SkippableFact]
    public async Task Recientes_multi_empresa_de_retenciones_mezcla_empresas()
    {
        Skip.IfNot(DbOk(), "PeachEBills local no disponible.");
        var tablero = new TableroComprobantes(new Factory());

        var filas = await tablero.RecientesAsync(
            new[] { "1792051800001", "1791709438001" }, "07", DateTime.Today.AddMonths(-3), DateTime.Today, top: 200,
            cancellationToken: default);

        Skip.If(filas.Count == 0, "La copia local no trae retenciones recientes.");
        Assert.All(filas, f => Assert.Contains(f.Ruc, new[] { "1792051800001", "1791709438001" }));
    }

    [SkippableFact]
    public async Task Panorama_de_retenciones_trae_la_ultima_emision_y_los_pendientes_los_aporta_el_llamador()
    {
        Skip.IfNot(DbOk(), "PeachEBills local no disponible.");
        var tablero = new TableroComprobantes(new Factory());

        var filas = await tablero.PanoramaAsync(
            new[] { ("1792051800001", "Empresa A"), ("00000000000000", "Inexistente") },
            "07",
            (ruc, ct) => Task.FromResult(ruc == "1792051800001" ? 7 : throw new InvalidOperationException("Cannot locate the named database")));

        var a = filas.Single(f => f.Ruc == "1792051800001");
        Assert.Equal(7, a.Pendientes);
        Assert.NotNull(a.UltimaEmision);

        var inexistente = filas.Single(f => f.Ruc == "00000000000000");
        Assert.Null(inexistente.UltimaEmision);
        Assert.NotNull(inexistente.Error);          // el fallo de Sage queda en la fila, no rompe el panorama
    }

    [SkippableFact]
    public async Task Los_pendientes_de_retencion_no_se_descuentan_de_Facturas()
    {
        Skip.IfNot(DbOk(), "PeachEBills local no disponible.");
        var tablero = new TableroComprobantes(new Factory());

        Assert.Empty(await tablero.PostOrdersEmitidosAsync("1792051800001", "07"));
        Assert.Empty(await tablero.PostOrdersEmitidosAsync(new[] { "1792051800001" }, "07"));
    }
}
