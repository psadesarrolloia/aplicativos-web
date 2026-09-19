using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PsaWeb.Conciliacion.Data;

namespace PsaWeb.Conciliacion;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra el staging de comprobantes del SRI (<c>ComprobantesSriDescargados</c>)
    /// sobre la misma base física de <c>PsaWebPlataforma</c> — no es una base
    /// nueva, comparte la cadena de conexión de la sección <c>Plataforma</c>.
    /// </summary>
    public static IServiceCollection AddConciliacion(this IServiceCollection services, IConfiguration configuration)
    {
        var cs = configuration.GetSection("Plataforma")["ConnectionString"];
        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException("Falta la cadena de conexión. Configure 'Plataforma:ConnectionString'.");
        }

        services.AddDbContext<ConciliacionDbContext>(o => o.UseSqlServer(cs));
        services.AddScoped<IRepositorioComprobantesSri, RepositorioComprobantesSri>();
        services.AddScoped<ILectorComprobantesSri, LectorComprobantesSri>();
        services.AddScoped<IRepositorioRevisionesConciliacion, RepositorioRevisionesConciliacion>();

        return services;
    }
}
