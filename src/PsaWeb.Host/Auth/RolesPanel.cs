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
