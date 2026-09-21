using Microsoft.Extensions.DependencyInjection;
using PsaWeb.Modules.Reportes.Cheques;

namespace PsaWeb.Modules.Reportes;

public static partial class ReportesModule
{
    static partial void AgregarCheques(IServiceCollection services, bool muestra)
    {
        if (muestra)
        {
            services.AddScoped<IChequesRepository, SampleChequesRepository>();
        }
        else
        {
            services.AddScoped<IChequesRepository, OdbcChequesRepository>();
        }
        services.AddSingleton<ChequePdfRenderer>();
        services.AddScoped<ServicioImpresionCheques>();
    }
}
