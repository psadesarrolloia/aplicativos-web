using Microsoft.EntityFrameworkCore;
using PsaWeb.Modules.Ventas.Prefacturas;
using PsaWeb.Ventas.Prefacturas;

namespace PsaWeb.Ventas.Tests;

/// <summary>
/// Persistencia real en SQL Server (PsaWebPlataforma local). <b>Escribe y borra</b> filas de una empresa de prueba (<c>TEST-F2-…</c>) y aplica la migración
/// pendiente a esa base: solo con <c>PSAWEB_TEST_PLATAFORMA</c> = cadena de conexión (p. ej. <c>Server=.\SQLEXPRESS;Database=PsaWebPlataforma;Trusted_Connection=True;TrustServerCertificate=True</c>).
/// </summary>
public class AlmacenPrefacturasEfTests
{
    private sealed class Fabrica(DbContextOptions<VentasDbContext> opciones) : IDbContextFactory<VentasDbContext>
    {
        public VentasDbContext CreateDbContext() => new(opciones);
    }

    private static Prefactura Nueva(string ruc, string cliente = "CLIENTE") => new(
        0, ruc, "Empresa de prueba", 0, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 17), EstadoPrefactura.Emitida,
        cliente, "Cliente de prueba", "Contacto", "099", "c@x.com", 2, 30, "Net 30 Days", "WILSON JACHO", "JACHO WILSON (EQU)", "OC", "Dirección", "Nota", "Interna",
        "4-15%", 15m, 153.43m, 23.01m, 176.44m,
        new[]
        {
            new LineaPrefactura(1, "ITEM-1", "Descripción 1", "UND", 2m, 6.95m, 6.95m, false, 13.90m, 30m),
            new LineaPrefactura(2, "ITEM-2", "Descripción 2", "UND", 3m, 48.96m, 46.51m, true, 139.53m, null),
        },
        "vendedor1", new DateTime(2026, 10, 2, 15, 0, 0, DateTimeKind.Utc), EstadoCorreo.Pendiente, string.Empty, null, null, null, null, null);

    [SkippableFact]
    public async Task Guarda_numera_en_paralelo_lee_y_actualiza_en_SQL_Server()
    {
        var cs = Environment.GetEnvironmentVariable("PSAWEB_TEST_PLATAFORMA");
        Skip.If(string.IsNullOrWhiteSpace(cs), "Sin PSAWEB_TEST_PLATAFORMA.");
        var fabrica = new Fabrica(new DbContextOptionsBuilder<VentasDbContext>().UseSqlServer(cs).Options);
        var ruc = "TEST-F2-" + Guid.NewGuid().ToString("N")[..8];
        var almacen = new AlmacenPrefacturasEf(fabrica);

        await using (var db = fabrica.CreateDbContext()) await db.Database.MigrateAsync();
        try
        {
            // 8 vendedores emitiendo a la vez: los números deben salir 1..8 sin repetirse (índice único + reintento).
            var guardadas = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => almacen.GuardarAsync(Nueva(ruc, "C" + i))));
            Assert.Equal(Enumerable.Range(1, 8), guardadas.Select(g => g.Numero).Order());
            Assert.All(guardadas, g => Assert.True(g.Id > 0));

            var p = (await almacen.ObtenerAsync(ruc, guardadas[0].Id))!;
            Assert.Equal(2, p.Lineas.Count);
            Assert.Equal((153.43m, 23.01m, 176.44m), (p.Subtotal, p.Iva, p.Total));
            Assert.True(p.Lineas[1].PrecioManual);
            Assert.Equal(48.96m, p.Lineas[1].PrecioLista);
            Assert.Null(p.Lineas[1].ExistenciaAlEmitir);
            Assert.Equal(new DateOnly(2026, 10, 17), p.ValidaHasta);

            await almacen.RegistrarCorreoAsync(ruc, p.Id, EstadoCorreo.Enviado, "a@x.com, b@x.com", null, new DateTime(2026, 10, 2, 16, 0, 0, DateTimeKind.Utc));
            await almacen.MarcarFacturadaAsync(ruc, p.Id, "001-003-000013539", "contadora", new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc));
            var f = (await almacen.ObtenerAsync(ruc, p.Id))!;
            Assert.Equal(EstadoPrefactura.Facturada, f.Estado);
            Assert.Equal("001-003-000013539", f.FacturaSage);
            Assert.Equal(EstadoCorreo.Enviado, f.CorreoEstado);
            Assert.Equal("a@x.com, b@x.com", f.CorreoDestinatarios);

            Assert.Equal(8, (await almacen.ListarAsync(ruc, new FiltroPrefacturas())).Count);
            Assert.Single(await almacen.ListarAsync(ruc, new FiltroPrefacturas(Texto: "C3")));
            Assert.Null(await almacen.ObtenerAsync("OTRA-EMPRESA", p.Id));

            Assert.Equal(15, (await almacen.ConfiguracionAsync(ruc)).VigenciaDias);
            await almacen.GuardarConfiguracionAsync(ConfiguracionVentas.PorDefecto(ruc) with { CorreoContabilidad = "conta@x.com", VigenciaDias = 20 }, "admin");
            var c = await almacen.ConfiguracionAsync(ruc);
            Assert.Equal(("conta@x.com", 20), (c.CorreoContabilidad, c.VigenciaDias));
        }
        finally
        {
            // Solo las filas de esta corrida (empresa de prueba única).
            await using var db = fabrica.CreateDbContext();
            await db.Prefacturas.Where(x => x.Ruc == ruc).ExecuteDeleteAsync();
            await db.ConfiguracionesVentas.Where(x => x.Ruc == ruc).ExecuteDeleteAsync();
        }
    }
}
