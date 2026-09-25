using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PsaWeb.Modules.Compras.Servicios;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.Compras;

public static class ComprasModule
{
    /// <summary>
    /// Registra el módulo Compras. Requiere Sage 50 (ODBC), PeachEBills (catálogos) y la cola del Sage Bridge
    /// (<c>AddSageBridgeCola</c>): el Host solo lo llama si están las tres.
    /// </summary>
    public static IServiceCollection AddCompras(this IServiceCollection services)
    {
        services.TryAddScoped<IResolverEmpresaSage, SinShellResolverEmpresaSage>();
        services.AddScoped<ServicioCompras>();
        services.AddScoped<ServicioRecibidos>();
        return services;
    }
}
