using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PsaWeb.Modules.Kardex.Data;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.Kardex;

public static class KardexModule
{
    /// <summary>
    /// Registra el módulo Kardex de inventarios (solo lectura, acotado a la
    /// empresa de sesión).
    /// </summary>
    /// <remarks>
    /// F1: solo repositorio de muestra. F2 agrega el repositorio ODBC real y el
    /// interruptor real/muestra según <c>Sage50:ConnectionString</c>.
    /// </remarks>
    public static IServiceCollection AddKardex(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IKardexRepository, SampleKardexRepository>();

        // Sin shell: la empresa es siempre la de configuración. El Host reemplaza
        // este registro por la implementación real cuando el shell está activo.
        services.TryAddScoped<IResolverEmpresaSage, SinShellResolverEmpresaSage>();

        return services;
    }
}
