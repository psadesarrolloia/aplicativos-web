using System.Data.Odbc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PsaWeb.Comprobantes.Compras;
using PsaWeb.Conciliacion;
using PsaWeb.Conciliacion.Data;
using PsaWeb.PeachEbills;
using PsaWeb.PeachEbills.Data;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.ConciliacionSri.Tests;

/// <summary>
/// El procesador degrada con gracia por empresa (no aborta la corrida entera)
/// — probado contra la base local <c>PeachEBills</c> real (se saltea si no
/// está disponible), con un RUC que no tiene fila en <c>PeachConnString</c>
/// para forzar el fallo de "configuración incompleta" sin necesitar Sage.
/// </summary>
public class ProcesadorVerificacionEstadoTests
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

    private sealed class EmpresasActivasFake(IReadOnlyList<EmpresaActiva> empresas) : IEmpresasActivasRepository
    {
        public Task<IReadOnlyList<EmpresaActiva>> ObtenerAsync(CancellationToken ct = default) =>
            Task.FromResult(empresas);
    }

    private sealed class LectorSriFake(IReadOnlyList<ComprobanteSriGuardado> filas) : ILectorComprobantesSri
    {
        public Task<IReadOnlyList<ComprobanteSriGuardado>> ObtenerAsync(
            string ruc, DateOnly desde, DateOnly hasta, CancellationToken ct = default) =>
            Task.FromResult(filas);
    }

    private sealed class RepositorioSriFake : IRepositorioComprobantesSri
    {
        public Task<ResultadoSubidaReporte> GuardarReporteAsync(
            string ruc, string contenidoReporte, string subidoPor, CancellationToken ct = default) =>
            throw new NotSupportedException("No hace falta para este test.");

        public Task ActualizarEstadoAsync(long id, string estado, DateTime fechaVerificacionUtc, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    /// <summary>Guarda en memoria, con la misma validación del comentario que el repositorio real.</summary>
    private sealed class RevisionesFake : IRepositorioRevisionesConciliacion
    {
        public Dictionary<string, RevisionConciliacion> Guardadas { get; } = new();

        public Task<IReadOnlyDictionary<string, RevisionConciliacion>> ListarAsync(string ruc, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, RevisionConciliacion>>(Guardadas);

        public Task RegistrarAsync(
            string ruc, string clave, string huella, string comentario, string revisadaPor, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(comentario))
            {
                throw new ArgumentException("El comentario es obligatorio.", nameof(comentario));
            }

            Guardadas[clave] = new RevisionConciliacion
            {
                Ruc = ruc, Clave = clave, Huella = huella, Comentario = comentario.Trim(), RevisadaPor = revisadaPor,
            };
            return Task.CompletedTask;
        }

        public Task QuitarAsync(string ruc, string clave, CancellationToken ct = default)
        {
            Guardadas.Remove(clave);
            return Task.CompletedTask;
        }
    }

    private sealed class VerificadorFake : IVerificadorEstadoSri
    {
        public int Llamadas { get; private set; }

        public Task<ResultadoVerificacionEstado> VerificarAsync(string claveAcceso, CancellationToken ct = default)
        {
            Llamadas++;
            return Task.FromResult(new ResultadoVerificacionEstado(EstadoComprobanteSri.Autorizado, null));
        }
    }

    private sealed class SageQueFalla : ISageConnectionFactory
    {
        public OdbcConnection CreateConnection() => throw new NotSupportedException();
        public OdbcConnection CreateConnection(string connectionString) => new("Driver={Driver Inexistente PSA};");
    }

    private static ComprobanteSriGuardado FilaSri(string clave) => new(
        Id: 1, ClaveAcceso: clave, RucEmisor: "1791111111001", RazonSocialEmisor: "PROVEEDOR DEMO S.A.",
        TipoComprobante: "Factura", SerieComprobante: "001-001-000000001",
        FechaAutorizacion: DateTime.Today, FechaEmision: DateOnly.FromDateTime(DateTime.Today),
        Subtotal: 100, Iva: 12, Total: 112, NumeroDocumentoModificado: null,
        Estado: null, FechaVerificacionEstado: null);

    private static ProcesadorVerificacionEstado Construir(
        IEmpresasActivasRepository empresas, ILectorComprobantesSri lectorSri, IVerificadorEstadoSri verificador,
        IRepositorioRevisionesConciliacion? revisiones = null) =>
        new(
            empresas,
            new PeachConnStringResolver(new Factory()),
            new SageQueFalla(),
            lectorSri,
            verificador,
            new RepositorioSriFake(),
            revisiones ?? new RevisionesFake(),
            Options.Create(new ConciliacionOptions()),
            NullLogger<ProcesadorVerificacionEstado>.Instance);

    [SkippableFact]
    public async Task Ruc_sin_configuracion_de_conexion_degrada_con_gracia()
    {
        Skip.IfNot(DbDisponible(), "PeachEBills local no disponible.");

        const string rucInexistente = "0000000000000";
        var procesador = Construir(
            new EmpresasActivasFake([new EmpresaActiva(rucInexistente, "Empresa Inexistente", 2)]),
            new LectorSriFake([FilaSri("0" + new string('1', 48))]),
            new VerificadorFake());

        var resultado = await procesador.ProcesarUnaAsync(rucInexistente, "Empresa Inexistente");

        var empresa = Assert.Single(resultado.Empresas);
        Assert.Equal(1, empresa.ConErrores);
        Assert.Equal(0, empresa.Verificados);
        Assert.Contains(empresa.Mensajes, m => m.Contains("No hay cadena de conexión ODBC"));
    }

    [SkippableFact]
    public async Task Sin_filas_pendientes_no_llama_al_verificador()
    {
        Skip.IfNot(DbDisponible(), "PeachEBills local no disponible.");

        var verificador = new VerificadorFake();
        var procesador = Construir(
            new EmpresasActivasFake([new EmpresaActiva("0000000000000", "Empresa Sin Datos", 2)]),
            new LectorSriFake([]), // Set A vacío: nunca llega a intentar Sage.
            verificador);

        var resultado = await procesador.ProcesarUnaAsync("0000000000000", "Empresa Sin Datos");

        var empresa = Assert.Single(resultado.Empresas);
        Assert.Equal(0, empresa.ConErrores);
        Assert.Equal(0, verificador.Llamadas);
    }

    // ------------------------------------------------------------ revisión manual ("Aceptada / Revisada OK")

    private const string Ruc = "1799999999006";
    private const string Clave = "0109202601179111111100120010010000000011234567811";

    private static FilaConciliacion FilaDe(ClasificacionConciliacion c, bool conSri = true, bool conSage = true) => new(
        conSri ? Clave : null,
        conSri ? FilaSri(Clave) : null,
        conSage ? new CompraSage(5, "1791111111001", "PROV", "001-001-000000001", new DateOnly(2026, 9, 1), Clave, 100, 12, 112) : null,
        c,
        c == ClasificacionConciliacion.ValoresDistintos ? ["Total: SRI 112.00 vs Sage 110.00"] : []);

    private static ProcesadorVerificacionEstado ProcesadorConRevisiones(RevisionesFake revisiones, params ComprobanteSriGuardado[] setA) =>
        Construir(new EmpresasActivasFake([]), new LectorSriFake(setA), new VerificadorFake(), revisiones);

    [Theory]
    [InlineData(ClasificacionConciliacion.SoloEnSri)]
    [InlineData(ClasificacionConciliacion.SoloEnSage)]
    [InlineData(ClasificacionConciliacion.ValoresDistintos)]
    [InlineData(ClasificacionConciliacion.MetadataDistinta)]
    public async Task Se_puede_revisar_cada_una_de_las_4_categorias_con_algo_que_revisar(ClasificacionConciliacion c)
    {
        var revisiones = new RevisionesFake();
        var procesador = ProcesadorConRevisiones(revisiones);
        var fila = FilaDe(c, conSri: c != ClasificacionConciliacion.SoloEnSage, conSage: c != ClasificacionConciliacion.SoloEnSri);

        await procesador.RevisarAsync(Ruc, fila, "Corresponde a ICE", "lparedes");

        var guardada = Assert.Single(revisiones.Guardadas).Value;
        Assert.Equal(ClaveRevision.De(fila), guardada.Clave);
        Assert.Equal(ClaveRevision.Huella(fila), guardada.Huella);
        Assert.Equal("lparedes", guardada.RevisadaPor);
    }

    [Fact]
    public async Task Una_fila_conciliada_no_se_puede_aceptar()
    {
        var procesador = ProcesadorConRevisiones(new RevisionesFake());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            procesador.RevisarAsync(Ruc, FilaDe(ClasificacionConciliacion.CoincidePendienteDeVerificar), "ok", "lparedes"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Revisar_exige_comentario(string comentario)
    {
        var revisiones = new RevisionesFake();
        var procesador = ProcesadorConRevisiones(revisiones);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            procesador.RevisarAsync(Ruc, FilaDe(ClasificacionConciliacion.ValoresDistintos), comentario, "lparedes"));
        Assert.Empty(revisiones.Guardadas);
    }

    [Fact]
    public async Task Quitar_revision_borra_la_marca()
    {
        var revisiones = new RevisionesFake();
        var procesador = ProcesadorConRevisiones(revisiones);
        var fila = FilaDe(ClasificacionConciliacion.ValoresDistintos);
        await procesador.RevisarAsync(Ruc, fila, "ok", "lparedes");

        await procesador.QuitarRevisionAsync(Ruc, fila);

        Assert.Empty(revisiones.Guardadas);
    }

    [Fact]
    public async Task Sin_comprobantes_del_SRI_la_conciliacion_con_revisiones_es_vacia_y_no_toca_Sage()
    {
        var procesador = ProcesadorConRevisiones(new RevisionesFake()); // Set A vacío: Sage (que falla) no se usa.

        var resultado = await procesador.ConciliarConRevisionesAsync(Ruc, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.Empty(resultado.Filas);
        Assert.Empty(resultado.Advertencias);
    }
}
