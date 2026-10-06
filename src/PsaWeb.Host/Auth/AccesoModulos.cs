using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PsaWeb.Identidad;
using PsaWeb.Notificaciones;
using PsaWeb.Seguridad;

namespace PsaWeb.Host.Auth;

/// <summary>
/// Guardia de los endpoints de descarga (Excel, PDF, XML) de un módulo: exige empresa (<c>?ruc=</c>), acceso a esa empresa y alguna
/// llave del módulo según <see cref="AppCatalogo"/>, igual que la guardia de páginas. Antes bastaba con tener sesión: sin <c>ruc</c> se
/// exportaba la empresa fija de <c>Sage50:ConnectionString</c> (hallazgo AW-1, 2026-10-05).
/// Sin directorio de seguridad (desarrollo sin PeachEBills) no filtra.
/// </summary>
public sealed class FiltroAccesoModulo(string appId) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var seguridad = http.RequestServices.GetService<ISecurityDirectory>();
        if (seguridad is null)
        {
            return await next(context);
        }

        var veredicto = await EvaluarAsync(http.User, http.Request.Query["ruc"].ToString(), appId, seguridad, http.RequestAborted);
        return veredicto switch
        {
            VeredictoAcceso.Permitido => await next(context),
            VeredictoAcceso.FaltaEmpresa => Results.BadRequest("Falta la empresa (ruc)."),
            _ => Results.StatusCode(StatusCodes.Status403Forbidden),
        };
    }

    public static async Task<VeredictoAcceso> EvaluarAsync(
        ClaimsPrincipal usuario, string? ruc, string appId, ISecurityDirectory seguridad, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ruc)) return VeredictoAcceso.FaltaEmpresa;
        if (ClaimsPsa.DebeActivarSegundoFactor(usuario)) return VeredictoAcceso.Denegado;
        var nombre = usuario.Identity?.Name ?? string.Empty;
        if (!(await seguridad.EmpresasDelUsuarioAsync(nombre, ct)).Any(e => e.Ruc == ruc)) return VeredictoAcceso.Denegado;
        var app = AppCatalogo.PorId(appId) ?? throw new InvalidOperationException($"Módulo desconocido: {appId}");
        var ctx = await seguridad.ContextoAsync(nombre, ruc, ct);
        return app.VisiblePara(ctx) ? VeredictoAcceso.Permitido : VeredictoAcceso.Denegado;
    }
}

public enum VeredictoAcceso { Permitido, FaltaEmpresa, Denegado }

public static class AccesoModulosExtensions
{
    /// <summary>El endpoint pertenece al módulo <paramref name="appId"/> de <see cref="AppCatalogo"/>: aplica <see cref="FiltroAccesoModulo"/>.</summary>
    public static RouteHandlerBuilder ExigirModulo(this RouteHandlerBuilder builder, string appId)
    {
        _ = AppCatalogo.PorId(appId) ?? throw new ArgumentException($"Módulo desconocido: {appId}", nameof(appId));
        return builder.AddEndpointFilter(new FiltroAccesoModulo(appId));
    }

    /// <summary>
    /// Super Admin / Admin sin verificación en dos pasos: solo pueden usar su cuenta (para activarla) y salir. Las páginas HTML se
    /// redirigen a /mi-cuenta/seguridad; el resto responde 403. Dentro del circuito de Blazor lo mismo lo hace <c>GuardiaModulo</c>.
    /// </summary>
    public static IApplicationBuilder UseSegundoFactorObligatorio(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        if (ClaimsPsa.DebeActivarSegundoFactor(http.User) && !RutaPermitidaSinSegundoFactor(http.Request.Path))
        {
            if (HttpMethods.IsGet(http.Request.Method) && http.Request.Headers.Accept.ToString().Contains("text/html"))
            {
                http.Response.Redirect("/mi-cuenta/seguridad?obligatorio=1");
            }
            else
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
            }
            return;
        }
        await next();
    });

    public static bool RutaPermitidaSinSegundoFactor(PathString path)
    {
        var p = path.Value ?? "/";
        if (p.StartsWith("/mi-cuenta", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/salir", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/_content", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        // Archivos estáticos (css, js, imágenes): el último segmento tiene extensión.
        var ultimo = p[(p.LastIndexOf('/') + 1)..];
        return ultimo.Contains('.');
    }
}

/// <summary>
/// Re-valida cada 5 minutos la sesión de los circuitos de Blazor (que no vuelven a pasar por la cookie): si la cuenta fue
/// deshabilitada o cambió su sello de seguridad (clave, perfil, 2FA, correo), el circuito queda sin usuario.
/// </summary>
public sealed class RevalidacionSesion(ILoggerFactory loggers, IServiceScopeFactory scopes, IOptions<IdentityOptions> identity)
    : RevalidatingServerAuthenticationStateProvider(loggers)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(5);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var users = scope.ServiceProvider.GetService<UserManager<UsuarioApp>>();
        if (users is null) return true; // sin plataforma (modo standalone)
        var u = await users.GetUserAsync(state.User);
        if (u is null || !u.Activo) return false;
        if (!users.SupportsUserSecurityStamp) return true;
        var sello = state.User.FindFirstValue(identity.Value.ClaimsIdentity.SecurityStampClaimType);
        return sello == await users.GetSecurityStampAsync(u);
    }
}

/// <summary>Correos de la cuenta por el SMTP de <c>PsaWeb.Notificaciones</c> (si está registrado y configurado).</summary>
public sealed class EnviadorCorreoNotificaciones(IServiceProvider sp) : IEnviadorCorreoPlataforma
{
    private IServicioCorreo? Smtp => sp.GetService<IServicioCorreo>();

    public bool Disponible => Smtp?.Disponible == true;

    public Task EnviarAsync(string para, string asunto, string cuerpoHtml, CancellationToken cancellationToken = default) =>
        (Smtp ?? throw new InvalidOperationException("El correo saliente no está configurado."))
            .EnviarAsync(new MensajeCorreo(new[] { para }, asunto, cuerpoHtml), cancellationToken);
}
