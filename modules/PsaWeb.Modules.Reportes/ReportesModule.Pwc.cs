using Microsoft.Extensions.DependencyInjection;
using PsaWeb.Modules.Reportes.Pwc;

namespace PsaWeb.Modules.Reportes;

public static partial class ReportesModule
{
    static partial void AgregarPwc(IServiceCollection services, bool muestra)
    {
        if (muestra)
        {
            services.AddScoped<IPwcRepository, SamplePwcRepository>();
        }
        else
        {
            services.AddScoped<IPwcRepository, OdbcPwcRepository>();
        }
        services.AddSingleton<PwcExcelExporter>();
    }
}
