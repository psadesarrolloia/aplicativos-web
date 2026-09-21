using Microsoft.Extensions.DependencyInjection;
using PsaWeb.Modules.Reportes.Comisiones;

namespace PsaWeb.Modules.Reportes;

public static partial class ReportesModule
{
    static partial void AgregarComisiones(IServiceCollection services, bool muestra)
    {
        if (muestra)
        {
            services.AddScoped<IComisionesRepository, SampleComisionesRepository>();
        }
        else
        {
            services.AddScoped<IComisionesRepository, OdbcComisionesRepository>();
        }
        services.AddSingleton<ComisionesExcelExporter>();
    }
}
