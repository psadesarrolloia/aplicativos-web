using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PsaWeb.Seguridad;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra el directorio de seguridad (empresas + permisos por usuario) y el
    /// estado de sesión de empresa/ambiente. Requiere <c>AddPeachEbills</c>.
    /// Con <c>Accesos:Fuente=Web</c> (y plataforma configurada) el directorio es la tabla propia de accesos web y desaparece el
    /// GateProvisional; si no, PeachEBills como siempre. Devuelve la fuente efectiva.
    /// </summary>
    public static FuenteAccesos AddSeguridad(this IServiceCollection services, IConfiguration? configuration = null, bool plataformaConfigurada = false)
    {
        var fuente = LeerFuente(configuration);
        if (!plataformaConfigurada)
        {
            fuente = FuenteAccesos.PeachEBills; // la tabla web vive en PsaWebPlataforma
        }
        AccesosOptions.AplicarModo(fuente);
        services.Configure<AccesosOptions>(o => o.Fuente = fuente);

        services.AddScoped<PeachEbillsSecurityDirectory>();
        services.AddScoped<ICatalogoEmpresas, CatalogoEmpresasPeachEbills>();
        if (fuente == FuenteAccesos.Web)
        {
            services.AddScoped<ISecurityDirectory, AccesosWebSecurityDirectory>();
        }
        else
        {
            services.AddScoped<ISecurityDirectory>(sp => sp.GetRequiredService<PeachEbillsSecurityDirectory>());
        }
        if (plataformaConfigurada)
        {
            services.AddScoped<ServicioAccesos>();
        }
        services.AddScoped<EmpresaActualService>();
        return fuente;
    }

    public static FuenteAccesos LeerFuente(IConfiguration? configuration) =>
        Enum.TryParse<FuenteAccesos>(configuration?[$"{AccesosOptions.SectionName}:Fuente"], ignoreCase: true, out var f)
            ? f
            : FuenteAccesos.PeachEBills;
}
