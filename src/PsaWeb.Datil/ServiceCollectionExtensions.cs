using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PsaWeb.Datil;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra el cliente de Datil (<see cref="IDatilClient"/>). Enlaza
    /// <see cref="DatilOptions"/> a la sección <c>Datil</c>: si no hay config,
    /// <c>DryRun</c> queda en <c>true</c> (no se envía nada).
    /// </summary>
    public static IServiceCollection AddDatil(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatilOptions>(configuration.GetSection(DatilOptions.SectionName));
        services.AddHttpClient<IDatilClient, DatilClient>(http =>
        {
            http.Timeout = TimeSpan.FromSeconds(60);
        });
        return services;
    }
}
