using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PsaWeb.Modules.Ventas.Data;
using PsaWeb.Modules.Ventas.Pages;
using PsaWeb.Modules.Ventas.Prefacturas;
using PsaWeb.Notificaciones;
using PsaWeb.Sage50;
using PsaWeb.Ventas.Prefacturas;

namespace PsaWeb.Ventas.Tests;

/// <summary>Renderiza de verdad las páginas de prefacturas (HtmlRenderer, sin HTTP ni login) con datos de muestra y almacén en memoria.</summary>
public class PaginasPrefacturaRenderTests
{
    private sealed class Auth : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "vendedor1") }, "test"))));
    }

    private sealed class FakeNav : NavigationManager
    {
        public string? Ultimo { get; private set; }
        public FakeNav() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, NavigationOptions options) => Ultimo = uri;
    }

    private sealed class CorreoFalso : IServicioCorreo
    {
        public bool Disponible => true;
        public List<MensajeCorreo> Enviados { get; } = new();
        public Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken = default) { Enviados.Add(mensaje); return Task.CompletedTask; }
    }

    private sealed class Activador : IComponentActivator
    {
        public IComponent? Pagina { get; private set; }

        public IComponent CreateInstance(Type componentType)
        {
            var c = (IComponent)Activator.CreateInstance(componentType)!;
            if (componentType.Namespace == "PsaWeb.Modules.Ventas.Pages") Pagina = c;
            return c;
        }
    }

    private sealed class Entorno
    {
        public ServiceProvider Sp { get; }
        public Activador Act { get; } = new();
        public FakeNav Nav { get; } = new();
        public CorreoFalso Correo { get; } = new();
        public AlmacenPrefacturasMemoria Almacen { get; } = new();

        public Entorno()
        {
            var s = new ServiceCollection();
            s.AddLogging();
            s.AddSingleton<IComponentActivator>(Act);
            s.AddSingleton<NavigationManager>(Nav);
            s.AddSingleton<AuthenticationStateProvider>(new Auth());
            s.AddScoped<IVentasRepository, SampleVentasRepository>();
            s.AddScoped<IResolverEmpresaSage, SinShellResolverEmpresaSage>();
            s.AddSingleton<IAlmacenPrefacturas>(Almacen);
            s.AddSingleton<IServicioCorreo>(Correo);
            s.AddSingleton(TimeProvider.System);
            s.AddScoped<ServicioPrefacturas>();
            Sp = s.BuildServiceProvider();
            Almacen.GuardarConfiguracionAsync(ConfiguracionVentas.PorDefecto("SIN-EMPRESA") with { CorreoContabilidad = "conta@sancev.test", CorreoAdicional = "gerencia@sancev.test" }, "t").Wait();
        }

        public async Task<string> RenderAsync<T>(Func<T, Task>? acciones = null, IDictionary<string, object?>? parametros = null) where T : IComponent
        {
            await using var renderer = new HtmlRenderer(Sp, Sp.GetRequiredService<ILoggerFactory>());
            return await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var salida = await renderer.RenderComponentAsync<T>(parametros is null ? ParameterView.Empty : ParameterView.FromDictionary(parametros));
                if (acciones is not null)
                {
                    await acciones((T)Act.Pagina!);
                    typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Act.Pagina, null);
                    await renderer.Dispatcher.InvokeAsync(() => Task.CompletedTask);
                }
                return salida.ToHtmlString();
            });
        }
    }

    private static T Campo<T>(object pagina, string nombre) =>
        (T)pagina.GetType().GetField(nombre, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pagina)!;

    private static void Poner(object pagina, string nombre, object valor) =>
        pagina.GetType().GetField(nombre, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(pagina, valor);

    private static async Task Llamar(object pagina, string metodo, params object?[] args)
    {
        var r = pagina.GetType().GetMethod(metodo, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pagina, args);
        if (r is Task t) await t;
    }

    /// <summary>Recorre el formulario como un vendedor: cliente → ítems → emitir.</summary>
    private static async Task LlenarYEmitir(PrefacturaNueva p)
    {
        Poner(p, "_textoCliente", "demo");
        await Llamar(p, "BuscarClientes");
        var cliente = Campo<IReadOnlyList<ClienteVenta>>(p, "_clientes").Single();
        await Llamar(p, "ElegirCliente", cliente);

        Poner(p, "_textoItem", "breaker 320");
        await Llamar(p, "BuscarItems");
        await Llamar(p, "Agregar", Campo<IReadOnlyList<ItemVenta>>(p, "_resultadosItems").Single());
        await Llamar(p, "Emitir");
    }

    [Fact]
    public async Task Nueva_prefactura_muestra_el_formulario_y_pide_elegir_cliente()
    {
        var html = await new Entorno().RenderAsync<PrefacturaNueva>();
        Assert.Contains("Nueva prefactura (cotizaci", html);
        Assert.Contains("Buscar cliente", html);
        Assert.Contains("primero el cliente", html);
    }

    [Fact]
    public async Task Elegir_el_cliente_propone_su_vendedor_y_la_etiqueta()
    {
        var e = new Entorno();
        var html = await e.RenderAsync<PrefacturaNueva>(async p =>
        {
            Poner(p, "_textoCliente", "demo");
            await Llamar(p, "BuscarClientes");
            await Llamar(p, "ElegirCliente", Campo<IReadOnlyList<ClienteVenta>>(p, "_clientes").Single());
        });
        Assert.Contains("CLIENTE DEMO S.A.", html);
        Assert.Contains("UNO VENDEDOR (EQU)", html); // etiqueta sugerida: apellido nombre (línea)
    }

    [Fact]
    public async Task El_precio_de_la_linea_es_el_de_la_lista_del_cliente_y_el_manual_se_marca()
    {
        var e = new Entorno();
        var html = await e.RenderAsync<PrefacturaNueva>(async p =>
        {
            Poner(p, "_textoCliente", "nivel 2");
            await Llamar(p, "BuscarClientes");
            await Llamar(p, "ElegirCliente", Campo<IReadOnlyList<ClienteVenta>>(p, "_clientes").Single());
            Poner(p, "_textoItem", "breaker 320");
            await Llamar(p, "BuscarItems");
            await Llamar(p, "Agregar", Campo<IReadOnlyList<ItemVenta>>(p, "_resultadosItems").Single());
            var linea = Campo<System.Collections.IList>(p, "_lineas")[0]!;
            Assert.Equal(325m, (decimal)linea.GetType().GetProperty("Precio")!.GetValue(linea)!); // cliente de nivel 1 = lista 2
            linea.GetType().GetProperty("Precio")!.SetValue(linea, 300m);
            await Llamar(p, "Refrescar");
        });
        Assert.Contains("manual", html);
    }

    [Fact]
    public async Task Emitir_guarda_envia_el_correo_y_lleva_al_detalle()
    {
        var e = new Entorno();
        await e.RenderAsync<PrefacturaNueva>(LlenarYEmitir);

        var p = Assert.Single(await e.Almacen.ListarAsync("SIN-EMPRESA", new FiltroPrefacturas()));
        Assert.Equal("CLIENTE DEMO", p.ClienteId);
        Assert.Equal("vendedor1", p.CreadaPor);
        Assert.Equal(EstadoCorreo.Enviado, p.CorreoEstado);
        Assert.Single(e.Correo.Enviados);
        Assert.Equal($"/ventas/prefacturas/{p.Id}", e.Nav.Ultimo);
    }

    [Fact]
    public async Task Sin_lineas_no_se_puede_emitir()
    {
        var e = new Entorno();
        var html = await e.RenderAsync<PrefacturaNueva>(async p =>
        {
            Poner(p, "_textoCliente", "demo");
            await Llamar(p, "BuscarClientes");
            await Llamar(p, "ElegirCliente", Campo<IReadOnlyList<ClienteVenta>>(p, "_clientes").Single());
            await Llamar(p, "Emitir");
        });
        Assert.Empty(await e.Almacen.ListarAsync("SIN-EMPRESA", new FiltroPrefacturas()));
        Assert.Contains("Falta completar", html);
    }

    [Fact]
    public async Task El_detalle_muestra_datos_para_Sage_PDF_y_permite_cerrar_con_la_factura()
    {
        var e = new Entorno();
        await e.RenderAsync<PrefacturaNueva>(LlenarYEmitir);
        var p = (await e.Almacen.ListarAsync("SIN-EMPRESA", new FiltroPrefacturas())).Single();

        var html = await e.RenderAsync<PrefacturaDetalle>(null, new Dictionary<string, object?> { ["Id"] = p.Id });
        Assert.Contains("PF-0001", html);
        Assert.Contains("Datos para la factura de Sage", html);
        Assert.Contains("BR-2049", html);
        Assert.Contains($"/ventas/prefacturas/{p.Id}/pdf?ruc=SIN-EMPRESA", html);
        Assert.Contains("Marcar como facturada", html);

        var cerrado = await e.RenderAsync<PrefacturaDetalle>(async d =>
        {
            Poner(d, "_facturaSage", "001-003-000013539");
            await Llamar(d, "MarcarFacturada");
        }, new Dictionary<string, object?> { ["Id"] = p.Id });
        Assert.Contains("001-003-000013539", cerrado);
        Assert.DoesNotContain("Marcar como facturada", cerrado);
    }

    [Fact]
    public async Task El_detalle_de_una_prefactura_inexistente_avisa()
    {
        var html = await new Entorno().RenderAsync<PrefacturaDetalle>(null, new Dictionary<string, object?> { ["Id"] = 999 });
        Assert.Contains("No existe esa prefactura", html);
    }

    [Fact]
    public async Task La_lista_muestra_las_prefacturas_de_la_empresa()
    {
        var e = new Entorno();
        await e.RenderAsync<PrefacturaNueva>(LlenarYEmitir);
        var html = await e.RenderAsync<ListaPrefacturas>();
        Assert.Contains("PF-0001", html);
        Assert.Contains("CLIENTE DEMO S.A.", html);
        Assert.Contains("Vigente", html);
        Assert.Contains("Enviado", html);
    }
}
