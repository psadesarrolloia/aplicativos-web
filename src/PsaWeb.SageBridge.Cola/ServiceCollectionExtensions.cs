using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PsaWeb.SageBridge.Cola.Data;

namespace PsaWeb.SageBridge.Cola;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra la cola del Sage Bridge sobre la base <c>PsaWebPlataforma</c> (cadena de la sección
    /// <c>Plataforma</c>, igual que Conciliación y Reportes).
    /// </summary>
    public static IServiceCollection AddSageBridgeCola(this IServiceCollection services, IConfiguration configuration)
    {
        var cs = configuration.GetSection("Plataforma")["ConnectionString"];
        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException("Falta la cadena de conexión. Configure 'Plataforma:ConnectionString'.");
        }

        services.AddDbContextFactory<SageBridgeDbContext>(o => o.UseSqlServer(cs));
        services.AddSingleton<IColaSage, ColaSage>();
        return services;
    }
}
