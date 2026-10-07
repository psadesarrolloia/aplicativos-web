using Microsoft.Extensions.Options;
using PsaWeb.Identidad;
using PsaWeb.Seguridad;

namespace PsaWeb.Host.Auth;

/// <summary>Rol de panel de un usuario leído de la base (no de la cookie). Sin servicio de accesos, solo vale el respaldo <c>Plataforma:Admins</c>.</summary>
public static class RolesPanel
{
    public static async Task<ServicioAccesos.Actor?> ActorAsync(IServiceProvider servicios, string? usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return null;
        // /admin solo desde las redes permitidas (Publico:AdminIpsPermitidas). Dentro del circuito de Blazor no pasa el middleware: la IP es la
        // de la conexión que abrió el circuito. Fuera de esas redes la cuenta no tiene panel (menú y páginas de admin).
        if (servicios.GetService<PublicacionOptions>() is { } publico
            && !PublicacionExtensions.AdminPermitido(publico, servicios.GetService<IHttpContextAccessor>()?.HttpContext?.Connection.RemoteIpAddress))
        {
            return null;
        }
        if (servicios.GetService<ServicioAccesos>() is { } accesos)
        {
            return await accesos.ActorAsync(usuario);
        }
        return servicios.GetService<IOptions<PlataformaOptions>>()?.Value.EsAdmin(usuario) == true
            ? new ServicioAccesos.Actor("", usuario, RolPanel.SuperAdmin)
            : null;
    }

    public static async Task<RolPanel> RolAsync(IServiceProvider servicios, string? usuario) =>
        (await ActorAsync(servicios, usuario))?.Rol ?? RolPanel.Ninguno;
}
