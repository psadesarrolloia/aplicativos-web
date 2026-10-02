using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PsaWeb.Modules.Ventas.Data;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.Ventas;

public static class VentasModule
{
    /// <summary>
    /// Registra el portal de ventas (F1: inventario en tiempo real y precios por nivel, solo lectura, empresa de sesión). Usa el
    /// repositorio ODBC cuando hay cadena de conexión y <c>Sage50:UseSampleData</c> no es <c>true</c>; si no, el de muestra.
    /// </summary>
    public static IServiceCollection AddVentas(this IServiceCollection services, IConfiguration configuration)
    {
        if (UsaDatosDeMuestra(configuration)) services.AddScoped<IVentasRepository, SampleVentasRepository>();
        else services.AddScoped<IVentasRepository, OdbcVentasRepository>();

        services.TryAddScoped<IResolverEmpresaSage, SinShellResolverEmpresaSage>();
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
