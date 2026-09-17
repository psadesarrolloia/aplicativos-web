using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace PsaWeb.Identidad;

/// <summary>
/// Autentica un endpoint por <c>Authorization: Bearer &lt;token&gt;</c> en vez de la
/// cookie de Identity — para la extensión de Chrome del módulo de Conciliación
/// SRI, que no puede depender de forma confiable de la cookie de sesión del
/// sitio. El endpoint que lo use debe marcarse <c>.AllowAnonymous()</c> (la
/// policy de cookie del sitio exige usuario autenticado por defecto; acá la
/// autenticación la hace este filtro, no el pipeline de Identity).
/// </summary>
public sealed class TokenExtensionEndpointFilter : IEndpointFilter
{
    /// <summary>Clave en <c>HttpContext.Items</c> donde queda el <c>UsuarioId</c> ya validado.</summary>
    public const string ItemUsuarioId = "TokenExtension.UsuarioId";

    private const string EsquemaBearer = "Bearer ";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var encabezado = context.HttpContext.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(encabezado) || !encabezado.StartsWith(EsquemaBearer, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Unauthorized();
        }

        var token = encabezado[EsquemaBearer.Length..].Trim();
        var servicio = context.HttpContext.RequestServices.GetRequiredService<IServicioTokensExtension>();
        var usuarioId = await servicio.ValidarAsync(token, context.HttpContext.RequestAborted);
        if (usuarioId is null)
        {
            return Results.Unauthorized();
        }

        context.HttpContext.Items[ItemUsuarioId] = usuarioId;
        return await next(context);
    }
}
