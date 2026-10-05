using PsaWeb.Seguridad;

namespace PsaWeb.Modules.Ventas.Prefacturas;

/// <summary>Permisos del usuario en el portal de ventas (<see cref="ReglasVentas"/>).</summary>
/// <param name="Cerrar">Contabilidad: ve todas las prefacturas, anota la factura de Sage y anula.</param>
public sealed record PermisosVentas(bool VerInventario, bool VerPrefacturas, bool Emitir, bool Cerrar)
{
    public static readonly PermisosVentas Todos = new(true, true, true, true);
    public static readonly PermisosVentas Ninguno = new(false, false, false, false);

    /// <summary>Un usuario sin «cerrar» solo ve las prefacturas que emitió él.</summary>
    public bool VerTodas => Cerrar;
}

/// <summary>Quién actúa: se pasa a cada operación del servicio para que los permisos se exijan en el servidor y no solo en el menú.</summary>
public sealed record ActorVentas(string Usuario, PermisosVentas Permisos)
{
    public static ActorVentas ConTodos(string usuario) => new(usuario, PermisosVentas.Todos);
}

public sealed class AccesoDenegadoVentasException(string mensaje) : UnauthorizedAccessException(mensaje);

/// <summary>Resuelve los permisos de un usuario en una empresa (<c>allowAction</c>/<c>adrAllowRol</c> de PeachEBills). Sin shell no hay directorio y se permite todo.</summary>
public sealed class ServicioPermisosVentas(IServiceProvider servicios)
{
    public async Task<PermisosVentas> PermisosAsync(string usuario, string ruc, CancellationToken ct = default)
    {
        if (servicios.GetService(typeof(ISecurityDirectory)) is not ISecurityDirectory dir) return PermisosVentas.Todos;
        var p = await dir.PermisosAsync(usuario, ruc, ct);
        return new PermisosVentas(ReglasVentas.PuedeVerInventario(p), ReglasVentas.PuedeVerPrefacturas(p), ReglasVentas.PuedeEmitir(p), ReglasVentas.PuedeCerrar(p));
    }

    public async Task<ActorVentas> ActorAsync(string usuario, string ruc, CancellationToken ct = default) =>
        new(usuario, await PermisosAsync(usuario, ruc, ct));
}
