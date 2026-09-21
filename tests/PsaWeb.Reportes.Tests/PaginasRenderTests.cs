using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PsaWeb.Modules.Reportes;
using PsaWeb.Modules.Reportes.Cheques;
using PsaWeb.Modules.Reportes.Comisiones;
using PsaWeb.Modules.Reportes.Comun;
using PsaWeb.Modules.Reportes.Pages;
using PsaWeb.Modules.Reportes.Pwc;
using PsaWeb.Sage50;

namespace PsaWeb.Reportes.Tests;

/// <summary>
/// Renderiza de verdad las 3 páginas Razor (HtmlRenderer, sin HTTP ni login) con los repositorios de muestra,
/// dispara «Consultar»/«Buscar» y verifica el HTML resultante. Cubre los errores de ejecución que el compilador no ve
/// (bindings, estados, componentes compartidos).
/// </summary>
public class PaginasRenderTests
{
    private sealed class EmpresaFake : IEmpresaSesionInfo
    {
        public string? Nombre => "RADIO FM DEMO CIA. LTDA.";
    }

    private sealed class Auth : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "tester") }, "test"))));
    }

    private sealed class FakeNav : NavigationManager
    {
        public string? Ultimo { get; private set; }
        public FakeNav() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, NavigationOptions options) => Ultimo = uri;
    }

    private sealed class FakeJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => default;
    }

    /// <summary>Captura la instancia de cada componente que crea el renderer, para poder llamar a sus métodos.</summary>
    private sealed class Activador : IComponentActivator
    {
        public IComponent? Ultimo { get; private set; }
        public IComponent CreateInstance(Type componentType)
        {
            var c = (IComponent)Activator.CreateInstance(componentType)!;
            if (componentType.Namespace == "PsaWeb.Modules.Reportes.Pages") Ultimo = c;
            return c;
        }
    }

    private static (ServiceProvider Sp, Activador Act, FakeNav Nav, IServicioConfiguracionReportes Cfg) Servicios()
    {
        var act = new Activador();
        var nav = new FakeNav();
        var cfg = new ServicioConfiguracionMemoria();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IComponentActivator>(act);
        services.AddSingleton<NavigationManager>(nav);
        services.AddSingleton<IJSRuntime>(new FakeJs());
        services.AddSingleton<AuthenticationStateProvider>(new Auth());
        services.AddSingleton<IServicioConfiguracionReportes>(cfg);
        services.AddSingleton<IResolverEmpresaSage, SinShellResolverEmpresaSage>();
        services.AddSingleton<IEmpresaSesionInfo, EmpresaFake>();
        services.AddSingleton<IPwcRepository, SamplePwcRepository>();
        services.AddSingleton<IComisionesRepository, SampleComisionesRepository>();
        services.AddSingleton<IChequesRepository, SampleChequesRepository>();
        return (services.BuildServiceProvider(), act, nav, cfg);
    }

    private static async Task<T> LlamarAsync<T>(object componente, string metodo, params object?[] args)
    {
        var m = componente.GetType().GetMethod(metodo, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new MissingMethodException(componente.GetType().Name, metodo);
        var r = m.Invoke(componente, args);
        if (r is Task t)
        {
            await t;
            return r is Task<T> tt ? tt.Result : default!;
        }
        return (T)r!;
    }

    private static void Poner(object componente, string campo, object? valor)
        => componente.GetType().GetField(campo, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(componente, valor);

    /// <summary>Renderiza la página, ejecuta <paramref name="accion"/> sobre su instancia y devuelve el HTML final.</summary>
    private static async Task<(string Html, FakeNav Nav)> Renderizar<TPagina>(
        Func<object, Task>? accion = null, Action<IServicioConfiguracionReportes>? prepara = null)
        where TPagina : IComponent
    {
        var (sp, act, nav, cfg) = Servicios();
        prepara?.Invoke(cfg);
        await using var _ = sp;
        await using var renderer = new HtmlRenderer(sp, sp.GetRequiredService<ILoggerFactory>());

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var raiz = await renderer.RenderComponentAsync<TPagina>();
            if (accion is not null)
            {
                await accion(act.Ultimo!);
                // Los eventos reales re-renderizan solos; al llamar al método directo se pide a mano.
                await LlamarAsync<object>(act.Ultimo!, "StateHasChanged");
                await Task.Delay(50);
            }
            return (raiz.ToHtmlString(), nav);
        });
    }

    // --- PWC -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Pwc_render_inicial_muestra_filtros_ciudades_y_personalizacion()
    {
        var (html, _) = await Renderizar<Pwc>();

        Assert.Contains("Cuentas por cobrar (PWC)", html);
        Assert.Contains("RADIO FM DEMO CIA. LTDA.", html);
        Assert.Contains("Emisión desde", html);
        Assert.Contains("Ciudad de cobro", html);
        Assert.Contains("(sin ciudad)", html);
        Assert.Contains("Encabezado de la columna A", html);
        Assert.Contains("RADIO (RAZON SOCIAL)", html);        // valor por defecto configurable
        Assert.Contains("AGENCIA DEMO UNO S.A.", html);          // datalist de clientes
        Assert.DoesNotContain("EFEMEDIO", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Resultado", html);                // aún no se consultó
    }

    [Fact]
    public async Task Pwc_consultar_muestra_estadisticas_resumenes_tabla_y_totales()
    {
        var (html, _) = await Renderizar<Pwc>(async p => await LlamarAsync<object>(p, "Consultar"));

        Assert.Contains("Resultado", html);
        Assert.Contains("Monto a cobrar por PWC", html);
        Assert.Contains("4.571,75", html);                 // total a cobrar
        Assert.Contains("5.600,00", html);                 // total facturado
        Assert.Contains("001-001-000000101", html);
        Assert.Contains("RET. IR 1,75%", html);            // resumen dinámico
        Assert.Contains("RET. IVA (sin %)", html);
        Assert.Contains("Resumen por ciudad de cobro", html);
        Assert.Contains("Totales", html);
    }

    [Fact]
    public async Task Pwc_con_filtro_de_ciudad_sin_coincidencias_avisa()
    {
        var (html, _) = await Renderizar<Pwc>(async p =>
        {
            Poner(p, "_factura", "no-existe");
            await LlamarAsync<object>(p, "Consultar");
        });
        Assert.Contains("No hay facturas con saldo", html);
    }

    [Fact]
    public async Task Pwc_rango_de_fechas_invertido_da_error_y_no_consulta()
    {
        var (html, _) = await Renderizar<Pwc>(async p =>
        {
            Poner(p, "_emisionDesde", (DateOnly?)new DateOnly(2026, 9, 2));
            Poner(p, "_emisionHasta", (DateOnly?)new DateOnly(2026, 9, 1));
            await LlamarAsync<object>(p, "Consultar");
        });
        Assert.Contains("no puede ser anterior a", html);
    }

    [Fact]
    public async Task Pwc_exportar_guarda_la_personalizacion_y_navega_al_endpoint_con_los_filtros()
    {
        var (_, nav) = await Renderizar<Pwc>(async p =>
        {
            Poner(p, "_cliente", "demo uno");
            Poner(p, "_emisionDesde", (DateOnly?)new DateOnly(2026, 8, 1));
            await LlamarAsync<object>(p, "Consultar");
            await LlamarAsync<object>(p, "Exportar");
        });

        Assert.NotNull(nav.Ultimo);
        Assert.Contains("cartera/pwc/export?", nav.Ultimo);
        Assert.Contains("emisionDesde=2026-08-01", nav.Ultimo);
        Assert.Contains("cliente=demo%20uno", nav.Ultimo);
    }

    [Fact]
    public async Task Pwc_las_columnas_ocultas_por_la_configuracion_no_salen()
    {
        var (html, _) = await Renderizar<Pwc>(
            async p => await LlamarAsync<object>(p, "Consultar"),
            cfg => cfg.GuardarAsync("_sin-empresa", ClavesReporte.Pwc,
                new ConfiguracionPwc { MostrarRetenciones = false, MostrarAnunciante = false, Cobrador = "ACME" }, "t").GetAwaiter().GetResult());

        Assert.Contains("Monto a cobrar por ACME", html);
        Assert.DoesNotContain("<th class=\"num\">Ret. IR</th>", html);
        Assert.DoesNotContain("<th>Anunciante</th>", html);
        Assert.DoesNotContain("Resumen de retenciones", html);
    }

    // --- Comisiones ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Comisiones_render_inicial_explica_los_bugs_heredados()
    {
        var (html, _) = await Renderizar<Comisiones>();

        Assert.Contains("Comisiones por recibos de cobro", html);
        Assert.Contains("Recibo desde", html);
        Assert.Contains("C2 — Comparar el rango de recibos como número", html);
        Assert.Contains("C1 — Abono = lo que aplicó ese recibo", html);
    }

    [Fact]
    public async Task Comisiones_sin_rango_pide_un_acotador()
    {
        var (html, _) = await Renderizar<Comisiones>(async p => await LlamarAsync<object>(p, "Consultar"));
        Assert.Contains("Indique un rango de recibos o un rango de fechas del recibo", html);
    }

    [Fact]
    public async Task Comisiones_consultar_agrupa_por_cliente_y_totaliza()
    {
        var (html, _) = await Renderizar<Comisiones>(async p =>
        {
            Poner(p, "_reciboDesde", "5122");
            Poner(p, "_reciboHasta", "5146");
            await LlamarAsync<object>(p, "Consultar");
        });

        Assert.Contains("AGENCIA DEMO UNO S.A.", html);
        Assert.Contains("CYEDE CIA. LTDA.", html);            // el 513 entra por la comparación de texto (C2)
        Assert.Contains("001-001-003661", html);
        Assert.Contains("Total general", html);
        Assert.Contains("3.571,60", html);                    // abono total heredado
    }

    [Fact]
    public async Task Comisiones_con_C2_numerico_el_513_desaparece_y_con_C1_cambia_el_abono()
    {
        var (numerico, _) = await Renderizar<Comisiones>(async p =>
        {
            Poner(p, "_reciboDesde", "5122");
            Poner(p, "_reciboHasta", "5146");
            Poner(p, "_rangoNumerico", true);
            await LlamarAsync<object>(p, "Consultar");
        });
        Assert.DoesNotContain("CYEDE CIA. LTDA.", numerico);

        var (porRecibo, _) = await Renderizar<Comisiones>(async p =>
        {
            Poner(p, "_reciboDesde", "5122");
            Poner(p, "_reciboHasta", "5146");
            Poner(p, "_abonoPorRecibo", true);
            await LlamarAsync<object>(p, "Consultar");
        });
        Assert.Contains("1.200,00", porRecibo);
        Assert.DoesNotContain("3.571,60", porRecibo);
    }

    [Fact]
    public async Task Comisiones_exportar_manda_las_opciones_C1_y_C2_al_endpoint()
    {
        var (_, nav) = await Renderizar<Comisiones>(async p =>
        {
            Poner(p, "_reciboDesde", "5122");
            Poner(p, "_reciboHasta", "5146");
            Poner(p, "_rangoNumerico", true);
            await LlamarAsync<object>(p, "Consultar");
            await LlamarAsync<object>(p, "Exportar");
        });

        Assert.Contains("cartera/comisiones/export?", nav.Ultimo);
        Assert.Contains("reciboDesde=5122", nav.Ultimo);
        Assert.Contains("rangoNumerico=true", nav.Ultimo);
        Assert.Contains("abonoPorRecibo=false", nav.Ultimo);
    }

    // --- Cheques ------------------------------------------------------------------------------------------------

    private static async Task Buscar(object p)
    {
        Poner(p, "_desde", new DateOnly(2026, 9, 1));
        Poner(p, "_hasta", new DateOnly(2026, 9, 30));
        await LlamarAsync<object>(p, "Buscar");
    }

    [Fact]
    public async Task Cheques_render_inicial_trae_la_personalizacion_y_el_enlace_a_la_hoja_de_prueba()
    {
        var (html, _) = await Renderizar<Cheques>();

        Assert.Contains("Cheques y comprobantes de egreso", html);
        Assert.Contains("Hoja de prueba de impresión (PDF)", html);
        Assert.Contains("bancos/cheques/prueba", html);
        Assert.Contains("Corrección X (mm)", html);
        Assert.Contains("Ciudad del «ciudad, fecha»", html);
        Assert.Contains("Courier New", html);
        Assert.DoesNotContain("Quito", html);
    }

    [Fact]
    public async Task Cheques_buscar_lista_por_defecto_solo_los_cheques_numericos_y_los_selecciona()
    {
        var (html, _) = await Renderizar<Cheques>(Buscar);

        Assert.Contains("TONY VERA", html);
        Assert.Contains("VERONICA ROSERO", html);
        Assert.DoesNotContain("PROVEEDOR CON UN NOMBRE MUY LARGO", html);  // tipo PI-: filtrado por defecto a «Default»
        Assert.Contains("2 seleccionado(s)", html);
        Assert.Contains("bancos/cheques/pdf?po=5003&amp;po=5002&amp;cheque=true&amp;comprobante=true", html);
        Assert.Contains("Vista previa", html);
        Assert.Contains("PI- (1)", html);                                   // el tipo aparece en el selector con su cantidad
    }

    [Fact]
    public async Task Cheques_elegir_el_tipo_PI_muestra_ese_pago_y_avisa_del_bug_Q1()
    {
        var (html, _) = await Renderizar<Cheques>(async p =>
        {
            await Buscar(p);
            Poner(p, "_tipo", "PI-");
            await LlamarAsync<object>(p, "Filtrar");
        });

        Assert.Contains("PROVEEDOR CON UN NOMBRE MUY LARGO", html);
        Assert.DoesNotContain("TONY VERA", html);
        Assert.Contains("bug Q1", html);                                     // 1,50 < $2,00
    }

    [Fact]
    public async Task Cheques_sin_nada_seleccionado_no_ofrece_imprimir()
    {
        var (html, _) = await Renderizar<Cheques>(async p =>
        {
            await Buscar(p);
            await LlamarAsync<object>(p, "SeleccionarTodos", false);
        });

        Assert.Contains("0 seleccionado(s)", html);
        Assert.DoesNotContain("href=\"bancos/cheques/pdf", html);
    }

    [Fact]
    public async Task Cheques_rango_de_fechas_invertido_da_error()
    {
        var (html, _) = await Renderizar<Cheques>(async p =>
        {
            Poner(p, "_desde", new DateOnly(2026, 9, 30));
            Poner(p, "_hasta", new DateOnly(2026, 9, 1));
            await LlamarAsync<object>(p, "Buscar");
        });
        Assert.Contains("no puede ser anterior a", html);
    }

    [Fact]
    public async Task Cheques_la_configuracion_guardada_de_la_empresa_se_carga_en_el_formulario()
    {
        var (html, _) = await Renderizar<Cheques>(prepara: cfg =>
        {
            cfg.GuardarAsync("_sin-empresa", ClavesReporte.Empresa, new ConfiguracionEmpresa { NombreEmpresa = "MI EMISORA", Ciudad = "Loja" }, "t").GetAwaiter().GetResult();
            cfg.GuardarAsync("_sin-empresa", ClavesReporte.Cheques, new ConfiguracionCheque { CorreccionX = 2.5, Firma2 = "AUTORIZADO" }, "t").GetAwaiter().GetResult();
        });

        Assert.Contains("MI EMISORA", html);
        Assert.Contains("Loja", html);
        Assert.Contains("AUTORIZADO", html);
        Assert.Contains("2.5", html);
    }
}
