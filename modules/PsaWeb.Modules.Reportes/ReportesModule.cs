using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PsaWeb.Modules.Reportes.Comun;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.Reportes;

public static partial class ReportesModule
{
    /// <summary>
    /// Registra el módulo de reportes de Access migrados (PWC, Comisiones y Cheques): solo lectura de
    /// Sage 50, acotado a la empresa de sesión. Usa los repositorios reales (ODBC) cuando hay cadena de
    /// conexión y <c>Sage50:UseSampleData</c> no es <c>true</c>; si no, los de muestra. La configuración
    /// por empresa vive en <c>PsaWebPlataforma</c> si hay <c>Plataforma:ConnectionString</c>; si no, en memoria.
    /// </summary>
    public static IServiceCollection AddReportes(this IServiceCollection services, IConfiguration configuration)
    {
        // Sin shell: la empresa es siempre la de configuración. El Host reemplaza este registro
        // por la implementación real cuando el shell está activo.
        services.TryAddScoped<IResolverEmpresaSage, SinShellResolverEmpresaSage>();
        services.TryAddScoped<IEmpresaSesionInfo, SinEmpresaSesionInfo>();
        services.AddScoped<SageAcceso>();

        var plataforma = configuration.GetSection("Plataforma")["ConnectionString"];
        if (string.IsNullOrWhiteSpace(plataforma))
        {
            services.AddSingleton<IServicioConfiguracionReportes, ServicioConfiguracionMemoria>();
        }
        else
        {
            services.AddDbContext<ReportesDbContext>(o => o.UseSqlServer(plataforma));
            services.AddScoped<IServicioConfiguracionReportes, ServicioConfiguracionEf>();
        }

        var muestra = UsaDatosDeMuestra(configuration);
        AgregarPwc(services, muestra);
        AgregarComisiones(services, muestra);
        AgregarCheques(services, muestra);

        return services;
    }

    /// <summary>true si el módulo va a usar datos de muestra (sin tocar Sage 50).</summary>
    public static bool UsaDatosDeMuestra(IConfiguration configuration)
    {
        var section = configuration.GetSection(SageOptions.SectionName);
        var forzarMuestra = string.Equals(section["UseSampleData"], "true", StringComparison.OrdinalIgnoreCase);
        return forzarMuestra || string.IsNullOrWhiteSpace(section["ConnectionString"]);
    }

    /// <summary>true si la configuración por empresa se persiste (hay plataforma) y hay que migrar.</summary>
    public static bool UsaPlataforma(IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(configuration.GetSection("Plataforma")["ConnectionString"]);

    // Los 3 reportes registran sus servicios en sus propios archivos parciales (F2..F4).
    static partial void AgregarPwc(IServiceCollection services, bool muestra);
    static partial void AgregarComisiones(IServiceCollection services, bool muestra);
    static partial void AgregarCheques(IServiceCollection services, bool muestra);
}
