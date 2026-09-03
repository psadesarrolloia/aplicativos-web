using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PsaWeb.Comprobantes.Retenciones;
using PsaWeb.Modules.Retenciones.Data;

namespace PsaWeb.Modules.Retenciones;

/// <summary>Marcador de ensamblado para el descubrimiento de rutas del módulo.</summary>
public static class ModuleInfo
{
}

public static class RetencionesModule
{
    /// <summary>
    /// Registra el módulo de Retenciones: lookups contra PeachEBills, el
    /// <see cref="RetencionBuilder"/>, el repositorio de pendientes, el
    /// orquestador, el candado single-flight y el worker en segundo plano.
    /// Requiere <c>AddPeachEbills</c>, <c>AddDatil</c> y <c>AddSage50</c>.
    /// </summary>
    public static IServiceCollection AddRetenciones(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RetencionesOptions>(configuration.GetSection(RetencionesOptions.SectionName));

        services.AddScoped<IEstablecimientoLookup, EstablecimientoLookupEf>();
        services.AddScoped<IInfoAdicionalLookup, InfoAdicionalLookupEf>();
        services.AddScoped<RetencionBuilder>();
        services.AddScoped<PendientesRepository>();
        services.AddScoped<EmpresaLookup>();
        services.AddScoped<RepositorioRetenciones>();
        services.AddScoped<ProcesadorRetenciones>();
        services.AddScoped<TableroRetenciones>();

        // Candado compartido (botón «Ejecutar ahora» + worker) y el worker mismo.
        // El worker arranca siempre pero se autolimita si Worker:Habilitado = false.
        services.AddSingleton<EjecucionRetencionesGate>();
        services.AddHostedService<RetencionesWorker>();

        return services;
    }
}
