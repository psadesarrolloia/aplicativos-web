using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PsaWeb.Comprobantes.Retenciones;
using PsaWeb.Comprobantes.Venta;
using PsaWeb.Comprobantes.Venta.InfoAdicional;
using PsaWeb.Modules.FacturacionElectronica.Data;

namespace PsaWeb.Modules.FacturacionElectronica;

public static class FacturacionElectronicaModule
{
    /// <summary>
    /// Registra el módulo de Facturación Electrónica (facturas de venta, notas de
    /// crédito y liquidaciones de compra). Requiere <c>AddPeachEbills</c>,
    /// <c>AddDatil</c> y <c>AddSage50</c>.
    /// </summary>
    public static IServiceCollection AddFacturacionElectronica(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FacturacionElectronicaOptions>(
            configuration.GetSection(FacturacionElectronicaOptions.SectionName));

        // Lookups compartidos con el módulo Retenciones (mismas tablas): TryAdd por si ya están.
        services.TryAddScoped<IEstablecimientoLookup, EstablecimientoLookupEf>();
        services.TryAddScoped<IInfoAdicionalLookup, InfoAdicionalLookupEf>();

        // Lookups propios de FE.
        services.AddScoped<IConfigInfoAdicionalFactura, ConfigInfoAdicionalFacturaEf>();
        services.AddScoped<ITasaIvaLookup, TasaIvaLookupEf>();

        services.AddScoped<FacturaBuilder>();
        services.AddScoped<NotaCreditoBuilder>();
        services.AddScoped<LiquidacionBuilder>();

        services.AddScoped<EmisorLookup>();
        services.AddScoped<ProcesadorComprobantesVenta>();

        return services;
    }
}
