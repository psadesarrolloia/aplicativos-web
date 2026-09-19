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

        public Task AceptarDiferenciaAsync(long id, string aceptadaPor, string? comentario, CancellationToken ct = default) =>
            throw new NotSupportedException("No hace falta para este test.");

        public Task QuitarAceptacionAsync(long id, CancellationToken ct = default) =>
            throw new NotSupportedException("No hace falta para este test.");
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
        IEmpresasActivasRepository empresas, ILectorComprobantesSri lectorSri, IVerificadorEstadoSri verificador) =>
        new(
            empresas,
            new PeachConnStringResolver(new Factory()),
            new SageQueFalla(),
            lectorSri,
            verificador,
            new RepositorioSriFake(),
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
}
