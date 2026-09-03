using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    /// <see cref="RetencionBuilder"/> y el repositorio de pendientes.
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

        return services;
    }
}
