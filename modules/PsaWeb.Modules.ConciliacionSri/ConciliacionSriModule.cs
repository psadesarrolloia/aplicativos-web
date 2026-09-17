using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PsaWeb.Conciliacion;

namespace PsaWeb.Modules.ConciliacionSri;

public static class ConciliacionSriModule
{
    /// <summary>
    /// Registra el procesador de verificación, el cliente SOAP, el candado
    /// single-flight y el worker en segundo plano. Requiere que ya estén
    /// registrados <c>AddConciliacion</c> (staging — solo necesita
    /// <c>Plataforma:ConnectionString</c>, se registra aparte y antes),
    /// <c>AddPeachEbills</c> y <c>AddSage50</c>.
    /// </summary>
    public static IServiceCollection AddConciliacionSri(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ConciliacionOptions>(configuration.GetSection(ConciliacionOptions.SectionName));

        services.AddHttpClient<IVerificadorEstadoSri, ConsultaComprobanteClient>();

        services.AddScoped<ProcesadorVerificacionEstado>();
        services.AddSingleton<EjecucionVerificacionGate>();
        services.AddHostedService<VerificacionEstadoSriWorker>();

        return services;
    }
}
