using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PsaWeb.Identidad;

public static class ServiceCollectionExtensions
{
    public const string SectionName = "Plataforma";

    /// <summary>Nombre de la política de rate-limiting para los endpoints de login.</summary>
    public const string PoliticaLimiteLogin = "login";

    /// <summary>
    /// Registra la base <c>PsaWebPlataforma</c>, ASP.NET Core Identity local
    /// (<see cref="UsuarioApp"/>) con política de claves + lockout + TOTP, y
    /// <see cref="IProveedorAutenticacion"/>. NO fija el esquema por defecto:
    /// eso lo hace el Host en F-Shell-1 al montar las pantallas de login.
    /// </summary>
    public static IServiceCollection AddIdentidadPlataforma(
        this IServiceCollection services, IConfiguration configuration)
    {
        var cs = configuration.GetSection(SectionName)["ConnectionString"];
        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException(
                $"Falta la cadena de conexión. Configure '{SectionName}:ConnectionString'.");
        }

        services.AddDbContext<PlataformaDbContext>(o => o.UseSqlServer(cs));

        services.AddIdentityCore<UsuarioApp>(o =>
        {
            o.Password.RequiredLength = 12;
            o.Password.RequireDigit = true;
            o.Password.RequireLowercase = true;
            o.Password.RequireUppercase = true;
            o.Password.RequireNonAlphanumeric = true;
            o.Password.RequiredUniqueChars = 4;

            o.Lockout.AllowedForNewUsers = true;
            o.Lockout.MaxFailedAccessAttempts = 5;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

            o.User.RequireUniqueEmail = false;
            o.SignIn.RequireConfirmedAccount = false;
        })
        .AddEntityFrameworkStores<PlataformaDbContext>()
        .AddSignInManager()
        .AddDefaultTokenProviders();

        services.AddScoped<IProveedorAutenticacion, IdentityProveedorAutenticacion>();
        services.AddScoped<IdentidadSeeder>();

        return services;
    }

    /// <summary>
    /// Política de rate-limiting para el login: sólo cuenta los <c>POST</c>
    /// (los envíos de credenciales), 10 por IP cada 5 minutos. Los <c>GET</c> de
    /// la pantalla no consumen cupo. El bloqueo de cuenta (5 fallos / 15 min) es
    /// la defensa primaria; esto es defensa en profundidad contra fuerza bruta
    /// distribuida por usuario. Se llama desde <c>AddRateLimiter(...)</c> del Host.
    /// </summary>
    public static void AgregarPoliticaLimiteLogin(RateLimiterOptions opciones)
    {
        opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        opciones.AddPolicy(PoliticaLimiteLogin, contexto =>
        {
            if (!HttpMethods.IsPost(contexto.Request.Method))
            {
                return RateLimitPartition.GetNoLimiter("sin-limite");
            }

            var ip = contexto.Connection.RemoteIpAddress?.ToString() ?? "sin-ip";
            return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
            });
        });
    }
}
