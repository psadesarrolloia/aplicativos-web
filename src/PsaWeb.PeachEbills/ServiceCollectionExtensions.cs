using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.PeachEbills;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra el acceso a la base <c>PeachEBills</c>: enlaza <see cref="PeachEbillsOptions"/>
    /// a la sección <c>PeachEbills</c> de configuración y expone un
    /// <see cref="IDbContextFactory{TContext}"/> de <see cref="PeachEbillsContext"/>
    /// (contexto corto por operación — sirve para Blazor Server y para el worker).
    /// </summary>
    public static IServiceCollection AddPeachEbills(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetSection(PeachEbillsOptions.SectionName)["ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Falta la cadena de conexión. Configure '{PeachEbillsOptions.SectionName}:ConnectionString'.");
        }

        services.AddDbContextFactory<PeachEbillsContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<PeachConnStringResolver>();

        return services;
    }
}
