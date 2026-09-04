using Microsoft.Extensions.DependencyInjection;

namespace PsaWeb.Seguridad;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra el directorio de seguridad (empresas + permisos por usuario) y el
    /// estado de sesión de empresa/ambiente. Requiere <c>AddPeachEbills</c>.
    /// </summary>
    public static IServiceCollection AddSeguridad(this IServiceCollection services)
    {
        services.AddScoped<ISecurityDirectory, PeachEbillsSecurityDirectory>();
        services.AddScoped<EmpresaActualService>();
        return services;
    }
}
