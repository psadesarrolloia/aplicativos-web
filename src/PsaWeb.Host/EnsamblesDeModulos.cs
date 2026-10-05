using System.Reflection;

namespace PsaWeb.Host;

/// <summary>
/// Ensamblados de los módulos que aportan páginas Razor. Los usan <c>Routes.razor</c> (<c>AdditionalAssemblies</c>) y el mapeo de endpoints
/// (<c>AddAdditionalAssemblies</c>): hay que declarar un módulo en los dos sitios o su ruta da 404 en silencio, por eso salen de un único lugar.
/// Con <c>Escritura:Habilitada</c> apagado el módulo Compras (Compras, Facturas recibidas, Liquidación de importaciones) <b>no</b> se incluye: sus rutas no existen.
/// </summary>
public static class EnsamblesDeModulos
{
    public static Assembly[] Todos(bool escrituraHabilitada)
    {
        var lista = new List<Assembly>
        {
            typeof(PsaWeb.Modules.CierreDeCaja.ModuleInfo).Assembly,
            typeof(PsaWeb.Modules.Kardex.ModuleInfo).Assembly,
            typeof(PsaWeb.Modules.Reportes.ModuleInfo).Assembly,
            typeof(PsaWeb.Modules.ComprobantesElectronicos.ComprobantesElectronicosModule).Assembly,
            typeof(PsaWeb.Modules.Ats.ModuleInfo).Assembly,
            typeof(PsaWeb.Modules.ConciliacionSri.ModuleInfo).Assembly,
            typeof(PsaWeb.Modules.Ventas.ModuleInfo).Assembly,
        };
        if (escrituraHabilitada) lista.Add(typeof(PsaWeb.Modules.Compras.ModuleInfo).Assembly);
        return lista.ToArray();
    }
}
