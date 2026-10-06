using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PsaWeb.Recibidos.Data;

namespace PsaWeb.Recibidos;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Almacén de XML recibidos (tablas en <c>PsaWebPlataforma</c>, sección <c>Plataforma</c>), descarga por el WS del SRI y la cola
    /// de descarga en segundo plano.
    /// </summary>
    public static IServiceCollection AddRecibidos(this IServiceCollection services, IConfiguration configuration)
    {
        var cs = configuration.GetSection("Plataforma")["ConnectionString"];
        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException("Falta la cadena de conexión. Configura 'Plataforma:ConnectionString'.");
        }
        services.AddDbContextFactory<RecibidosDbContext>(o => o.UseSqlServer(cs));
        services.AddSingleton<IAlmacenXmlRecibidos, AlmacenXmlRecibidos>();
        services.AddHttpClient<IDescargadorXmlSri, DescargadorXmlSri>(c => c.Timeout = TimeSpan.FromSeconds(60));
        services.AddScoped<ServicioDescargaXml>();
        services.AddSingleton<ColaDescargaXml>();
        services.AddHostedService<TrabajadorDescargaXml>();
        return services;
    }
}
