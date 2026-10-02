using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PsaWeb.Modules.Ventas.Data;
using PsaWeb.Modules.Ventas.Pages;
using PsaWeb.Sage50;

namespace PsaWeb.Ventas.Tests;

/// <summary>Renderiza de verdad la página (HtmlRenderer, sin HTTP ni login) con el repositorio de muestra y ejecuta sus acciones.</summary>
public class PaginaInventarioRenderTests
{
    private sealed class Activador : IComponentActivator
    {
        public IComponent? Pagina { get; private set; }

        public IComponent CreateInstance(Type componentType)
        {
            var c = (IComponent)Activator.CreateInstance(componentType)!;
            if (componentType == typeof(Inventario)) Pagina = c;
            return c;
        }
    }

    private sealed class FakeNav : NavigationManager
    {
        public FakeNav() => Initialize("http://localhost/", "http://localhost/ventas/inventario");
        protected override void NavigateToCore(string uri, NavigationOptions options) { }
    }

    private static async Task<string> RenderAsync(Func<Inventario, Task> acciones)
    {
        var act = new Activador();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<NavigationManager>(new FakeNav()); // la subnavegación usa NavLink
        services.AddSingleton<IComponentActivator>(act);
        services.AddScoped<IVentasRepository, SampleVentasRepository>();
        services.AddScoped<IResolverEmpresaSage, SinShellResolverEmpresaSage>();
        await using var sp = services.BuildServiceProvider();

        await using var renderer = new HtmlRenderer(sp, sp.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var salida = await renderer.RenderComponentAsync<Inventario>();
            var pagina = (Inventario)act.Pagina!;
            await acciones(pagina);
            typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pagina, null);
            await renderer.Dispatcher.InvokeAsync(() => Task.CompletedTask);
            return salida.ToHtmlString();
        });
    }

    private static void Poner(Inventario p, string campo, object valor) =>
        typeof(Inventario).GetField(campo, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(p, valor);

    private static Task Llamar(Inventario p, string metodo, params object?[] args) =>
        (Task)typeof(Inventario).GetMethod(metodo, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(p, args)!;

    [Fact]
    public async Task La_pagina_carga_sin_resultados_hasta_consultar()
    {
        var html = await RenderAsync(_ => Task.CompletedTask);
        Assert.Contains("Inventario y precios", html);
        Assert.DoesNotContain("Resultado (", html);
    }

    [Fact]
    public async Task Consultar_muestra_existencia_y_listas_con_precio()
    {
        var html = await RenderAsync(p => Llamar(p, "Buscar"));
        Assert.Contains("BR-2049", html);
        Assert.Contains("Lista 1", html);
        Assert.Contains("Lista 2", html);
        Assert.DoesNotContain("Lista 4", html);
        Assert.Contains("TE-2250", html); // los ensamblados «TE» vienen incluidos por defecto
    }

    [Fact]
    public async Task Al_desmarcar_ensamblados_los_tableros_TE_desaparecen()
    {
        var html = await RenderAsync(async p =>
        {
            await Llamar(p, "CambiarEnsamblados", new ChangeEventArgs { Value = false });
            await Llamar(p, "Buscar");
        });
        Assert.Contains("BR-2049", html);
        Assert.DoesNotContain("TE-2250", html);
    }

    [Fact]
    public async Task Con_cliente_de_nivel_1_muestra_el_precio_de_la_lista_2()
    {
        var html = await RenderAsync(async p =>
        {
            Poner(p, "_textoCliente", "nivel 2");
            await Llamar(p, "BuscarClientes");
            var cliente = (await new SampleVentasRepository().BuscarClientesAsync("nivel 2")).Single();
            typeof(Inventario).GetMethod("ElegirCliente", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(p, new object[] { cliente });
            Poner(p, "_texto", "breaker 320");
            await Llamar(p, "Buscar");
        });
        Assert.Contains("Lista de precios <strong>2</strong>", html);
        Assert.Contains("325,00", html); // BR-2049, lista 2 (el cliente es de nivel 1, 0-based)
        Assert.DoesNotContain("CA-003", html); // el texto «breaker 320» sí filtra
    }

    [Fact]
    public async Task Una_busqueda_sin_coincidencias_avisa()
    {
        var html = await RenderAsync(async p => { Poner(p, "_texto", "zzzz"); await Llamar(p, "Buscar"); });
        Assert.Contains("coincide con la b", html);
    }
}
