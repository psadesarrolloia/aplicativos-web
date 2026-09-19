using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PsaWeb.Conciliacion;
using PsaWeb.Conciliacion.Data;
using PsaWeb.Datil;
using PsaWeb.Datil.Model;
using PsaWeb.Modules.ComprobantesElectronicos.Data;
using PsaWeb.Modules.ComprobantesElectronicos.Estado;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.ComprobantesElectronicos.Tests;

/// <summary>Reglas puras del seguimiento de anulaciones (F6) y de las discrepancias con PeachEBills.</summary>
public class MaquinaAnulacionTests
{
    private static readonly DateTime Solicitud = new(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(TipoComprobante.Factura)]
    [InlineData(TipoComprobante.Retencion)]
    [InlineData(TipoComprobante.NotaCredito)]
    [InlineData(TipoComprobante.Liquidacion)]
    public void Si_el_SRI_dice_ANULADO_la_solicitud_queda_anulada_en_los_4_tipos(TipoComprobante tipo)
        => Assert.Equal(ResolucionAnulacion.Anulada,
            MaquinaAnulacion.Evaluar(tipo, Solicitud, "Anulado", Solicitud.AddDays(1)));

    [Theory]
    [InlineData(TipoComprobante.Retencion)]
    [InlineData(TipoComprobante.NotaCredito)]
    public void Retenciones_y_NC_sin_aceptacion_del_receptor_quedan_sin_efecto_a_los_5_dias(TipoComprobante tipo)
    {
        Assert.Equal(ResolucionAnulacion.SigueAbierta,
            MaquinaAnulacion.Evaluar(tipo, Solicitud, "Autorizado", Solicitud.AddDays(4).AddHours(23)));
        Assert.Equal(ResolucionAnulacion.SinEfecto,
            MaquinaAnulacion.Evaluar(tipo, Solicitud, "Autorizado", Solicitud.AddDays(5)));
        Assert.Equal(ResolucionAnulacion.SinEfecto,
            MaquinaAnulacion.Evaluar(tipo, Solicitud, "Autorizado", Solicitud.AddDays(20)));
    }

    [Theory]
    [InlineData(TipoComprobante.Factura)]
    [InlineData(TipoComprobante.Liquidacion)]
    public void Facturas_y_liquidaciones_no_requieren_aceptacion_y_no_vencen_solas(TipoComprobante tipo)
    {
        Assert.False(MaquinaAnulacion.RequiereAceptacionDelReceptor(tipo));
        Assert.Equal(ResolucionAnulacion.SigueAbierta,
            MaquinaAnulacion.Evaluar(tipo, Solicitud, "Autorizado", Solicitud.AddDays(60)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Otro")]
    [InlineData("NoAutorizado")]
    [InlineData("FueraDeRango")]
    public void Un_estado_que_no_es_ni_Anulado_ni_Autorizado_no_cierra_la_solicitud(string? estado)
        => Assert.Equal(ResolucionAnulacion.SigueAbierta,
            MaquinaAnulacion.Evaluar(TipoComprobante.Retencion, Solicitud, estado, Solicitud.AddDays(30)));

    [Fact]
    public void DiasAbierta_cuenta_dias_completos_y_nunca_es_negativo()
    {
        Assert.Equal(0, MaquinaAnulacion.DiasAbierta(Solicitud, Solicitud.AddHours(23)));
        Assert.Equal(3, MaquinaAnulacion.DiasAbierta(Solicitud, Solicitud.AddDays(3).AddHours(5)));
        Assert.Equal(0, MaquinaAnulacion.DiasAbierta(Solicitud, Solicitud.AddDays(-2)));
    }

    [Theory]
    [InlineData("Anulado", true, "Anulado en el SRI pero vigente en PeachEBills")]
    [InlineData("NoAutorizado", true, "Anulado en el SRI pero vigente en PeachEBills")]
    [InlineData("Autorizado", false, "Anulado en PeachEBills pero vigente en el SRI")]
    [InlineData("Autorizado", true, null)]
    [InlineData("Anulado", false, null)]
    [InlineData(null, true, null)]          // sin verificar: no se afirma nada
    [InlineData("FueraDeRango", true, null)]
    public void Discrepancias_entre_el_SRI_y_PeachEBills(string? sri, bool validoLocal, string? esperada)
        => Assert.Equal(esperada, Discrepancias.Evaluar(sri, validoLocal));

    [Fact]
    public void Worker_verifica_lo_reciente_vencido_y_completa_el_historico_de_a_poco()
    {
        var hoy = new DateTime(2026, 9, 19);
        var ahora = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
        ComprobanteAVerificar C(int id, string emision, short amb = 2) =>
            new(TipoComprobante.Factura, "1791313747001", id, DateTime.Parse(emision), "d", amb);

        var guardados = new Dictionary<(string Ruc, int RefId), EstadoSri>
        {
            [("1791313747001", 2)] = new("Autorizado", "SRI", null, ahora.AddDays(-1), null),     // reciente y fresco: no
            [("1791313747001", 3)] = new("Autorizado", "SRI", null, ahora.AddDays(-9), null),     // reciente y vencido: si
            [("1791313747001", 5)] = new("Autorizado", "Datil", null, ahora.AddDays(-200), null), // historico ya verificado: no
        };

        var lista = ServicioEstadoSri.SeleccionarParaWorker(
            new[]
            {
                C(1, "2026-09-01"),            // reciente sin verificar: si
                C(2, "2026-09-05"),
                C(3, "2026-08-20"),
                C(4, "2026-03-01"),            // historico nunca verificado: si (cupo)
                C(5, "2026-02-01"),
                C(6, "2025-11-01"),            // historico nunca verificado: si (cupo)
                C(7, "2025-10-01"),            // historico nunca verificado: fuera del cupo de 2
                C(8, "2026-09-02", amb: 1),    // pruebas: no
                C(9, "2024-01-01"),            // fuera de los 2 anios: no
            },
            guardados, TimeSpan.FromDays(7), maxHistorico: 2, hoy, ahora);

        Assert.Equal(new[] { 1, 3, 4, 6 }, lista.Select(c => c.RefId).OrderBy(x => x));
    }
}

/// <summary>
/// Integracion del seguimiento contra <c>PsaWebPlataforma</c> local y PeachEBills local, con el SRI y Datil
/// simulados. Usa RefIds de prueba (&gt;= 900000000) y los limpia.
/// </summary>
public class ServicioAnulacionesTests : IAsyncLifetime
{
    private const string PlataformaCs =
        @"Server=.\SQLEXPRESS;Database=PsaWebPlataforma;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15";
    private const string PeachCs =
        @"Server=.\SQLEXPRESS;Database=PeachEBills;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15";
    private const string Ruc = "1791313747001"; // tiene DatilAPI en la copia local
    private const string Clave = "0109202601179131374700120010030000134872794314815";

    private static readonly DbContextOptions<ConciliacionDbContext> OpcionesDb =
        new DbContextOptionsBuilder<ConciliacionDbContext>().UseSqlServer(PlataformaCs).Options;

    private sealed class PeachFactory : IDbContextFactory<PeachEbillsContext>
    {
        private readonly DbContextOptions<PeachEbillsContext> _o =
            new DbContextOptionsBuilder<PeachEbillsContext>().UseSqlServer(PeachCs).Options;
        public PeachEbillsContext CreateDbContext() => new(_o);
    }

    private sealed class SriFake(EstadoComprobanteSri estado) : IVerificadorEstadoSri
    {
        public Task<ResultadoVerificacionEstado> VerificarAsync(string claveAcceso, CancellationToken ct = default)
            => Task.FromResult(new ResultadoVerificacionEstado(estado, estado.ToString(), "<xml/>"));
    }

    private sealed class DatilFake : IDatilClient
    {
        public Task<DatilConsultaResult> ConsultarComprobanteAsync(string id, DatilCredentials c, CancellationToken ct = default)
            => Task.FromResult(new DatilConsultaResult { Estado = "AUTORIZADO", RawResponse = "{\"clave_acceso\":\"" + Clave + "\"}" });
        public Task<DatilEmisionResult> EmitirRetencionAsync(Retencion r, DatilCredentials c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DatilEmisionResult> EmitirFacturaAsync(Factura f, DatilCredentials c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DatilEmisionResult> EmitirNotaCreditoAsync(NotaCredito n, DatilCredentials c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DatilEmisionResult> EmitirLiquidacionAsync(Liquidacion l, DatilCredentials c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> ConsultarEstadoAsync(string id, DatilCredentials c, CancellationToken ct = default) => Task.FromResult<string?>("AUTORIZADO");
    }

    private static bool Disponible()
    {
        try
        {
            using var a = new ConciliacionDbContext(OpcionesDb);
            using var b = new PeachFactory().CreateDbContext();
            return a.Database.CanConnect() && b.Database.CanConnect();
        }
        catch { return false; }
    }

    private static ServicioAnulaciones Crear(EstadoComprobanteSri sri)
    {
        var sp = new ServiceCollection()
            .AddSingleton(OpcionesDb)
            .AddSingleton<IVerificadorEstadoSri>(new SriFake(sri))
            .BuildServiceProvider();
        var peach = new PeachFactory();
        var estado = new ServicioEstadoSri(sp, peach, new DatilFake(), new EmisorLookup(peach), NullLogger<ServicioEstadoSri>.Instance);
        return new ServicioAnulaciones(sp, estado, NullLogger<ServicioAnulaciones>.Instance);
    }

    private static ComprobanteAVerificar Doc(TipoComprobante tipo, int refId, short ambiente = 2) =>
        new(tipo, Ruc, refId, DateTime.Today, "datil-id", ambiente);

    public async Task InitializeAsync()
    {
        if (!Disponible()) return;
        await using var db = new ConciliacionDbContext(OpcionesDb);
        await db.Database.MigrateAsync();
        await Limpiar(db);
    }

    public async Task DisposeAsync()
    {
        if (!Disponible()) return;
        await using var db = new ConciliacionDbContext(OpcionesDb);
        await Limpiar(db);
    }

    private static async Task Limpiar(ConciliacionDbContext db)
    {
        await db.SolicitudesAnulacion.Where(s => s.RefId >= 900000000).ExecuteDeleteAsync();
        await db.EstadosSriComprobantes.Where(s => s.RefId >= 900000000).ExecuteDeleteAsync();
    }

    [SkippableFact]
    public async Task Registrar_es_idempotente_y_no_sigue_comprobantes_de_pruebas()
    {
        Skip.IfNot(Disponible(), "PsaWebPlataforma / PeachEBills locales no disponibles.");
        var svc = Crear(EstadoComprobanteSri.Autorizado);

        var a = await svc.RegistrarAsync(Doc(TipoComprobante.Retencion, 900000001), "001-001-000000001", "juan");
        var b = await svc.RegistrarAsync(Doc(TipoComprobante.Retencion, 900000001), "001-001-000000001", "maria");
        Assert.NotNull(a);
        Assert.Equal(a!.Id, b!.Id);               // la misma solicitud abierta
        Assert.Equal("juan", b.SolicitadaPor);

        Assert.Null(await svc.RegistrarAsync(Doc(TipoComprobante.Factura, 900000002, ambiente: 1), "001-001-000000002", "juan"));

        var abiertas = await svc.ObtenerAbiertasAsync(TipoComprobante.Retencion, new[] { (Ruc, 900000001), (Ruc, 900000002) });
        Assert.Single(abiertas);
        Assert.Equal("001-001-000000001", abiertas[(Ruc, 900000001)].Numero);
    }

    [SkippableFact]
    public async Task Cancelar_cierra_la_solicitud_y_permite_abrir_otra()
    {
        Skip.IfNot(Disponible(), "PsaWebPlataforma / PeachEBills locales no disponibles.");
        var svc = Crear(EstadoComprobanteSri.Autorizado);

        var s = await svc.RegistrarAsync(Doc(TipoComprobante.Factura, 900000003), "001-001-000000003", "juan");
        Assert.True(await svc.CancelarAsync(s!.Id, "supervisor"));
        Assert.False(await svc.CancelarAsync(s.Id, "supervisor")); // ya no esta abierta
        Assert.Empty(await svc.ObtenerAbiertasAsync(TipoComprobante.Factura, new[] { (Ruc, 900000003) }));

        var otra = await svc.RegistrarAsync(Doc(TipoComprobante.Factura, 900000003), "001-001-000000003", "juan");
        Assert.NotEqual(s.Id, otra!.Id);            // el historial se conserva; hay una nueva abierta
    }

    [SkippableFact]
    public async Task El_seguimiento_marca_Anulada_cuando_el_SRI_dice_ANULADO()
    {
        Skip.IfNot(Disponible(), "PsaWebPlataforma / PeachEBills locales no disponibles.");
        var svc = Crear(EstadoComprobanteSri.Anulado);
        var s = await svc.RegistrarAsync(Doc(TipoComprobante.Retencion, 900000004), "001-001-000000004", "juan");

        var resumen = await svc.ActualizarAbiertasAsync();

        Assert.True(resumen.Anuladas >= 1);
        await using var db = new ConciliacionDbContext(OpcionesDb);
        var fila = await db.SolicitudesAnulacion.AsNoTracking().SingleAsync(x => x.Id == s!.Id);
        Assert.Equal(EstadoAnulacion.Anulada, fila.Estado);
        Assert.Equal("Anulado", fila.UltimoEstadoSri);
        Assert.Equal("worker", fila.ResueltaPor);
        Assert.NotNull(fila.FechaResolucion);

        // y el estado del comprobante quedo guardado con la clave recuperada de Datil
        var estado = await db.EstadosSriComprobantes.AsNoTracking().SingleAsync(e => e.RefId == 900000004);
        Assert.Equal("Anulado", estado.Estado);
        Assert.Equal(Clave, estado.ClaveAcceso);
    }

    [SkippableFact]
    public async Task Una_retencion_autorizada_tras_5_dias_queda_SinEfecto_pero_una_factura_sigue_abierta()
    {
        Skip.IfNot(Disponible(), "PsaWebPlataforma / PeachEBills locales no disponibles.");
        var svc = Crear(EstadoComprobanteSri.Autorizado);
        var ret = await svc.RegistrarAsync(Doc(TipoComprobante.Retencion, 900000005), "001-001-000000005", "juan");
        var fac = await svc.RegistrarAsync(Doc(TipoComprobante.Factura, 900000006), "001-001-000000006", "juan");
        await using (var db = new ConciliacionDbContext(OpcionesDb))
        {
            await db.SolicitudesAnulacion.Where(s => s.Id == ret!.Id || s.Id == fac!.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.FechaSolicitud, DateTime.UtcNow.AddDays(-6)));
        }

        var resumen = await svc.ActualizarAbiertasAsync();

        Assert.True(resumen.SinEfecto >= 1);
        await using var db2 = new ConciliacionDbContext(OpcionesDb);
        Assert.Equal(EstadoAnulacion.SinEfecto, (await db2.SolicitudesAnulacion.AsNoTracking().SingleAsync(x => x.Id == ret!.Id)).Estado);
        Assert.Equal(EstadoAnulacion.Solicitada, (await db2.SolicitudesAnulacion.AsNoTracking().SingleAsync(x => x.Id == fac!.Id)).Estado);
    }
}
