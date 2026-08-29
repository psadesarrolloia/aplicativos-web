using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PsaWeb.Modules.CierreDeCaja.Data;
using PsaWeb.Modules.CierreDeCaja.Export;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.CierreDeCaja;

public static class CierreDeCajaModule
{
    /// <summary>
    /// Registra el módulo Cierre de Caja. Usa el repositorio real (ODBC / Sage 50)
    /// cuando hay cadena de conexión y <c>Sage50:UseSampleData</c> no es <c>true</c>;
    /// en cualquier otro caso, el repositorio de muestra.
    /// </summary>
    public static IServiceCollection AddCierreDeCaja(this IServiceCollection services, IConfiguration configuration)
    {
        if (UsaDatosDeMuestra(configuration))
        {
            services.AddScoped<ICierreDeCajaRepository, SampleCierreDeCajaRepository>();
        }
        else
        {
            services.AddScoped<ICierreDeCajaRepository, OdbcCierreDeCajaRepository>();
        }

        services.AddSingleton<CierreExcelExporter>();

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
