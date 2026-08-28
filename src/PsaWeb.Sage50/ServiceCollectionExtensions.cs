using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PsaWeb.Sage50;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra el acceso a datos de Sage 50: enlaza <see cref="SageOptions"/> a la sección
    /// <c>Sage50</c> de configuración y expone <see cref="ISageConnectionFactory"/>.
    /// </summary>
    public static IServiceCollection AddSage50(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SageOptions>(configuration.GetSection(SageOptions.SectionName));
        services.AddSingleton<ISageConnectionFactory, OdbcSageConnectionFactory>();
        return services;
    }
}
