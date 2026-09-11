using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PsaWeb.Modules.Kardex.Data;
using PsaWeb.Modules.Kardex.Export;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.Kardex;

public static class KardexModule
{
    /// <summary>
    /// Registra el módulo Kardex de inventarios (solo lectura, acotado a la
    /// empresa de sesión). Usa el repositorio real (ODBC / Sage 50) cuando hay
    /// cadena de conexión y <c>Sage50:UseSampleData</c> no es <c>true</c>; en
    /// cualquier otro caso, el repositorio de muestra.
    /// </summary>
    public static IServiceCollection AddKardex(this IServiceCollection services, IConfiguration configuration)
    {
        if (UsaDatosDeMuestra(configuration))
        {
            services.AddScoped<IKardexRepository, SampleKardexRepository>();
        }
        else
        {
            services.AddScoped<IKardexRepository, OdbcKardexRepository>();
        }

        // Sin shell: la empresa es siempre la de configuración. El Host reemplaza
        // este registro por la implementación real cuando el shell está activo.
        services.TryAddScoped<IResolverEmpresaSage, SinShellResolverEmpresaSage>();

        services.AddSingleton<KardexExcelExporter>();

        return services;
    }

    /// <summary>true si el módulo va a usar datos de muestra (sin tocar Sage 50).</summary>
    public static bool UsaDatosDeMuestra(IConfiguration configuration)
    {
        var section = configuration.GetSection(SageOptions.SectionName);
        var forzarMuestra = string.Equals(section["UseSampleData"], "true", StringComparison.OrdinalIgnoreCase);
        return forzarMuestra || string.IsNullOrWhiteSpace(section["ConnectionString"]);
    }
}
