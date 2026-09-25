using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PsaWeb.Identidad;
using PsaWeb.PeachEbills.Data;
using PsaWeb.SageBridge.Cola;
using PsaWeb.SageBridge.Cola.Data;
using PsaWeb.SageBridge.Contratos;
using SageBridgePage = PsaWeb.Host.Components.Pages.Admin.SageBridge;

namespace PsaWeb.Host.Tests;

/// <summary>Renderiza /admin/sage-bridge con una cola falsa (HtmlRenderer, sin HTTP ni login).</summary>
public class SageBridgeAdminRenderTests
{
    private sealed class Auth(string usuario) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, usuario) }, "test"))));
    }

    private sealed class FakeNav : NavigationManager
    {
        public FakeNav() => Initialize("http://localhost/", "http://localhost/admin/sage-bridge");
        protected override void NavigateToCore(string uri, NavigationOptions options) { }
    }

    private sealed class EmpresasFake : IEmpresasActivasRepository
    {
        public Task<IReadOnlyList<EmpresaActiva>> ObtenerAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmpresaActiva>>(new[]
            {
                new EmpresaActiva("1792051800001", "CPTDC ECUADOR S.A.", 2),
                new EmpresaActiva("1793198281001", "ROLLER DANCE", 2),
            });
    }

    private sealed class ColaFake : IColaSage
    {
        public List<(string Ruc, string Tipo)> Encolados { get; } = new();

        public Task<ResultadoEncolar> EncolarAsync(string ruc, string tipo, string? payloadJson, string claveIdempotencia, string usuario, CancellationToken cancellationToken = default)
        {
            Encolados.Add((ruc, tipo));
            return Task.FromResult(new ResultadoEncolar(new TrabajoSage { Id = 99, Ruc = ruc, Tipo = tipo }, true));
        }

        public Task<TrabajoSage?> ObtenerAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult<TrabajoSage?>(null);

        public Task<IReadOnlyList<TrabajoSage>> ListarAsync(FiltroTrabajos filtro, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TrabajoSage>>(new[]
            {
                new TrabajoSage
                {
                    Id = 6, Ruc = "1792051800001", Tipo = TiposTrabajo.ProbarEmpresa, Estado = EstadosTrabajo.Hecho, Intentos = 1,
                    CreadoPor = "lparedes", CreadoUtc = DateTime.UtcNow,
                    ResultadoJson = "{\"Abierta\":false,\"Acceso\":\"Pending\",\"BaseDatos\":\"cptdcecuadorsa202525\",\"Compania\":\"PUEBAS CPTDC\",\"CuentasLeidas\":0,\"Mensaje\":\"Solicitud de acceso pendiente.\",\"SegundosApertura\":0}",
                },
                new TrabajoSage
                {
                    Id = 7, Ruc = "1793198281001", Tipo = TiposTrabajo.ProbarEmpresa, Estado = EstadosTrabajo.Error, Intentos = 1,
                    CreadoPor = "lparedes", CreadoUtc = DateTime.UtcNow, Error = "La base no está en SoloBases.",
                },
            });

        public Task<IReadOnlyList<ResumenCola>> ResumenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResumenCola>>(Array.Empty<ResumenCola>());

        public Task<bool> CancelarAsync(long id, string usuario, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> ReintentarAsync(long id, string usuario, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlyList<LatidoBridge>> LatidosAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LatidoBridge>>(new[]
            {
                new LatidoBridge
                {
                    Instancia = "PREDATOR/predator-dev", UltimoLatidoUtc = DateTime.UtcNow, Usuario = @"PREDATOR\svc",
                    VersionAnfitrion = "1.0.0.0", VersionLogica = "0.1.0-F1", Estado = "esperando trabajos",
                },
            });

        public Task<IReadOnlyList<EmpresaBridge>> EmpresasAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmpresaBridge>>(new[]
            {
                new EmpresaBridge { Ruc = "1792051800001", Habilitada = false, AccesoSage = "Pending", AccesoVerificadoUtc = DateTime.UtcNow },
            });

        public Task<EmpresaBridge> GuardarEmpresaAsync(string ruc, bool habilitada, string? ventana, string? nota, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmpresaBridge { Ruc = ruc, Habilitada = habilitada, Ventana = ventana });
    }

    private static async Task<string> RenderizarAsync(string usuario, ColaFake cola)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<NavigationManager>(new FakeNav());
        services.AddSingleton<AuthenticationStateProvider>(new Auth(usuario));
        services.AddSingleton<IOptions<PlataformaOptions>>(Options.Create(new PlataformaOptions { Admins = { "lparedes" } }));
        services.AddSingleton<IColaSage>(cola);
        services.AddSingleton<IEmpresasActivasRepository>(new EmpresasFake());
        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, sp.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var salida = await renderer.RenderComponentAsync<SageBridgePage>();
            return System.Net.WebUtility.HtmlDecode(salida.ToHtmlString()); // el renderer codifica las tildes
        });
    }

    [Fact]
    public async Task Admin_ve_latido_empresas_y_detalle_de_los_trabajos()
    {
        var html = await RenderizarAsync("lparedes", new ColaFake());

        Assert.Contains("PREDATOR/predator-dev", html);
        Assert.Contains("activo", html);                       // latido reciente
        Assert.Contains("CPTDC ECUADOR S.A.", html);           // empresas activas de PeachEBills
        Assert.Contains("ROLLER DANCE", html);
        Assert.Contains("Pending", html);                      // último acceso de Sage
        Assert.Contains("Solicitud de acceso pendiente.", html); // resultado de ProbarEmpresa legible
        Assert.Contains("La base no está en SoloBases.", html);
        Assert.Contains("Reintentar", html);                   // acción del trabajo en Error
    }

    [Fact]
    public async Task No_admin_no_ve_nada_del_bridge()
    {
        var html = await RenderizarAsync("otro", new ColaFake());

        Assert.Contains("no tiene acceso a la administración", html);
        Assert.DoesNotContain("PREDATOR/predator-dev", html);
    }
}
