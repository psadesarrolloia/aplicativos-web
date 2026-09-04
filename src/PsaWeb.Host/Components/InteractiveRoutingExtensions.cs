using Microsoft.AspNetCore.Components;

namespace PsaWeb.Host.Components;

internal static class InteractiveRoutingExtensions
{
    /// <summary>
    /// true salvo que el endpoint tenga <see cref="ExcludeFromInteractiveRoutingAttribute"/>.
    /// Las pantallas del shell que hacen login (cookie de Identity) deben renderizar
    /// como SSR estático, no sobre el circuito interactivo.
    /// </summary>
    public static bool AceptaRuteoInteractivo(this HttpContext context)
        => context.GetEndpoint()?.Metadata.GetMetadata<ExcludeFromInteractiveRoutingAttribute>() is null;
}
