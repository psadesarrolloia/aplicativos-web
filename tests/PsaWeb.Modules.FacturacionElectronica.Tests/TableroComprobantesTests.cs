using Microsoft.EntityFrameworkCore;
using PsaWeb.Modules.FacturacionElectronica.Data;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.FacturacionElectronica.Tests;

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
}
