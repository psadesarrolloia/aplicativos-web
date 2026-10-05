using PsaWeb.Host;
using PsaWeb.Seguridad;

namespace PsaWeb.Host.Tests;

/// <summary>
/// El interruptor <c>Escritura:Habilitada</c> mantiene fuera del servidor los módulos que escriben en Sage por el Bridge (Compras, Facturas recibidas, Liquidación de
/// importaciones) hasta el deploy de escritura, sin tocar los módulos ya desplegados ni el portal de ventas.
/// </summary>
public class InterruptorEscrituraTests
{
    private static ContextoDeUsuario TodosLosPermisos()
    {
        var todos = AppCatalogo.Todas.SelectMany(a => a.PermisosQueLaHabilitan).ToHashSet();
        return new ContextoDeUsuario("u", "1791313747001", todos);
    }

    [Fact]
    public void Apagado_el_menu_no_ofrece_ningun_modulo_de_escritura_ni_a_quien_tiene_todos_los_permisos()
    {
        var ids = AppCatalogo.Habilitadas(TodosLosPermisos(), escrituraHabilitada: false).Select(a => a.Id).ToHashSet();
        Assert.Empty(ids.Intersect(AppCatalogo.IdsDeEscritura));
        Assert.Equal(new[] { "compras", "compras-importaciones", "compras-recibidos" }, AppCatalogo.IdsDeEscritura.Order());
    }

    [Fact]
    public void Apagado_siguen_los_modulos_ya_desplegados_y_el_portal_de_ventas()
    {
        var ids = AppCatalogo.Habilitadas(TodosLosPermisos(), escrituraHabilitada: false).Select(a => a.Id).ToHashSet();
        foreach (var esperado in new[] { "kardex", "ventas-inventario", "ventas-prefacturas", "ats", "conciliacion-sri", "fe-facturas" })
        {
            if (AppCatalogo.Todas.Any(a => a.Id == esperado)) Assert.Contains(esperado, ids);
        }
        Assert.Contains("ventas-inventario", ids);
        Assert.Contains("ventas-prefacturas", ids);
        Assert.Contains("kardex", ids);
    }

    [Fact]
    public void Encendido_el_menu_vuelve_a_ofrecer_los_de_escritura_y_es_el_comportamiento_por_defecto_del_catalogo()
    {
        var conEscritura = AppCatalogo.Habilitadas(TodosLosPermisos(), escrituraHabilitada: true).Select(a => a.Id).ToHashSet();
        Assert.Superset(AppCatalogo.IdsDeEscritura.ToHashSet(), conEscritura);
        Assert.Equal(conEscritura, AppCatalogo.Habilitadas(TodosLosPermisos()).Select(a => a.Id).ToHashSet());
    }

    [Fact]
    public void Todos_los_ids_de_escritura_existen_en_el_catalogo()
    {
        var ids = AppCatalogo.Todas.Select(a => a.Id).ToHashSet();
        Assert.Subset(ids, AppCatalogo.IdsDeEscritura.ToHashSet());
    }

    [Fact]
    public void Apagado_las_rutas_del_modulo_Compras_no_existen()
    {
        var apagado = EnsamblesDeModulos.Todos(escrituraHabilitada: false);
        var encendido = EnsamblesDeModulos.Todos(escrituraHabilitada: true);
        var compras = typeof(PsaWeb.Modules.Compras.ModuleInfo).Assembly;

        Assert.DoesNotContain(compras, apagado);
        Assert.Contains(compras, encendido);
        Assert.Equal(encendido.Length - 1, apagado.Length);
        Assert.Contains(typeof(PsaWeb.Modules.Ventas.ModuleInfo).Assembly, apagado); // el portal de ventas sale con o sin escritura
        Assert.Contains(typeof(PsaWeb.Modules.ConciliacionSri.ModuleInfo).Assembly, apagado);
        Assert.Equal(apagado.Length, apagado.Distinct().Count());
    }

    [Fact]
    public void La_opcion_viene_apagada_por_defecto()
    {
        Assert.False(new EscrituraOptions().Habilitada);
        Assert.Equal("Escritura", EscrituraOptions.SectionName);
    }
}
