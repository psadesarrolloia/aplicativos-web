using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PsaWeb.Notificaciones;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="IServicioCorreo"/>. Si no hay <c>Correo:Servidor</c>,
    /// registra una impl deshabilitada (<see cref="IServicioCorreo.Disponible"/> = false;
    /// <c>EnviarAsync</c> lanza) para que la app arranque sin SMTP.
    /// </summary>
    public static IServiceCollection AddNotificaciones(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CorreoOptions>(configuration.GetSection(CorreoOptions.SectionName));
        services.AddSingleton<IServicioCorreo, SmtpServicioCorreo>();
        return services;
    }
}
