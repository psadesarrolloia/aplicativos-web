using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PsaWeb.Comprobantes.Retenciones;
using PsaWeb.Comprobantes.Venta;
using PsaWeb.Comprobantes.Venta.InfoAdicional;
using PsaWeb.Modules.ComprobantesElectronicos.Data;
using PsaWeb.Modules.ComprobantesElectronicos.Retenciones;
using PsaWeb.Modules.ComprobantesElectronicos.Retenciones.Data;

namespace PsaWeb.Modules.ComprobantesElectronicos;

public static class ComprobantesElectronicosModule
{
    /// <summary>
    /// Registra el módulo de Comprobantes electrónicos: facturas de venta,
    /// retenciones de compra, notas de crédito y liquidaciones de compra (cada uno
    /// un submódulo con la misma interfaz y funciones). Requiere
    /// <c>AddPeachEbills</c>, <c>AddDatil</c> y <c>AddSage50</c>.
    /// </summary>
    public static IServiceCollection AddComprobantesElectronicos(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FacturacionElectronicaOptions>(
            configuration.GetSection(FacturacionElectronicaOptions.SectionName));
        services.Configure<RetencionesOptions>(configuration.GetSection(RetencionesOptions.SectionName));

        // Lookups compartidos por los 4 tipos (mismas tablas de PeachEBills).
        services.AddScoped<IEstablecimientoLookup, EstablecimientoLookupEf>();
        services.AddScoped<IInfoAdicionalLookup, InfoAdicionalLookupEf>();
        services.AddScoped<IConfigInfoAdicionalFactura, ConfigInfoAdicionalFacturaEf>();
        services.AddScoped<ITasaIvaLookup, TasaIvaLookupEf>();

        // Venta: facturas, notas de crédito, liquidaciones.
        services.AddScoped<FacturaBuilder>();
        services.AddScoped<NotaCreditoBuilder>();
        services.AddScoped<LiquidacionBuilder>();
        services.AddScoped<EmisorLookup>();
        services.AddScoped<ProcesadorComprobantesVenta>();

        // Retenciones.
        services.AddScoped<RetencionBuilder>();
        services.AddScoped<PendientesRepository>();
        services.AddScoped<EmpresaLookup>();
        services.AddScoped<RepositorioRetenciones>();
        services.AddScoped<ProcesadorRetenciones>();
        services.AddScoped<TableroRetenciones>();

        // Candado compartido (lote manual + worker) y el worker mismo.
        // El worker arranca siempre pero se autolimita si Retenciones:Worker:Habilitado = false.
        services.AddSingleton<EjecucionRetencionesGate>();
        services.AddHostedService<RetencionesWorker>();

        // Común a los 4 tipos: listados, panorama por empresa, generar y solicitar anulación.
        services.AddScoped<TableroComprobantes>();
        services.AddScoped<ServicioComprobantes>();
        services.AddScoped<SolicitudAnulacion>();
        services.AddScoped<Estado.ServicioEstadoSri>();

        return services;
    }
}
