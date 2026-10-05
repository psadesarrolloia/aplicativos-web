using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PsaWeb.Modules.Ventas.Data;
using PsaWeb.Modules.Ventas.Prefacturas;
using PsaWeb.Notificaciones;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.Ventas;

public static class VentasModule
{
    /// <summary>
    /// Registra el portal de ventas: F1 (inventario en tiempo real y precios, solo lectura) y F2 (prefacturas con PDF y correo a Contabilidad; guardadas
    /// en <c>PsaWebPlataforma</c> si hay <c>Plataforma:ConnectionString</c>, si no en memoria). Usa el repositorio ODBC cuando hay cadena de conexión y
    /// <c>Sage50:UseSampleData</c> no es <c>true</c>; si no, el de muestra. Nada de esto escribe en Sage.
    /// </summary>
    public static IServiceCollection AddVentas(this IServiceCollection services, IConfiguration configuration)
    {
        if (UsaDatosDeMuestra(configuration)) services.AddScoped<IVentasRepository, SampleVentasRepository>();
        else services.AddScoped<IVentasRepository, OdbcVentasRepository>();

        services.TryAddScoped<IResolverEmpresaSage, SinShellResolverEmpresaSage>();

        var plataforma = configuration.GetSection("Plataforma")["ConnectionString"];
        if (string.IsNullOrWhiteSpace(plataforma))
        {
            services.AddSingleton<IAlmacenPrefacturas, AlmacenPrefacturasMemoria>();
        }
        else
        {
            services.AddDbContextFactory<VentasDbContext>(o => o.UseSqlServer(plataforma));
            services.AddScoped<IAlmacenPrefacturas, AlmacenPrefacturasEf>();
        }

        // Si el Host no registró el SMTP (modo sin plataforma) queda un correo inerte; AddNotificaciones, si se llama después, lo reemplaza.
        services.TryAddSingleton<IServicioCorreo, CorreoInerte>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ServicioPrefacturas>();
        services.AddScoped<ServicioPermisosVentas>();
        return services;
    }

    /// <summary>true si el módulo va a usar datos de muestra (sin tocar Sage 50).</summary>
    public static bool UsaDatosDeMuestra(IConfiguration configuration)
    {
        var section = configuration.GetSection(SageOptions.SectionName);
        var forzarMuestra = string.Equals(section["UseSampleData"], "true", StringComparison.OrdinalIgnoreCase);
        return forzarMuestra || string.IsNullOrWhiteSpace(section["ConnectionString"]);
    }

    private sealed class CorreoInerte : IServicioCorreo
    {
        public bool Disponible => false;

        public Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("El correo no está configurado (falta 'Correo:Servidor').");
    }
}
