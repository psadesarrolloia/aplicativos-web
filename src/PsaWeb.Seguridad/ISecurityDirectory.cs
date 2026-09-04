namespace PsaWeb.Seguridad;

/// <summary>
/// Directorio de seguridad: qué empresas ve un usuario y qué puede hacer en cada
/// una. Hoy lo respalda PeachEBills (<see cref="PeachEbillsSecurityDirectory"/>);
/// mañana podría ser Keycloak u otro, sin tocar el resto de la plataforma.
/// </summary>
public interface ISecurityDirectory
{
    /// <summary>Empresas a las que el usuario tiene acceso, ordenadas.</summary>
    Task<IReadOnlyList<EmpresaDelUsuario>> EmpresasDelUsuarioAsync(
        string usuario, CancellationToken cancellationToken = default);

    /// <summary>Códigos de permiso activos del usuario en una empresa.</summary>
    Task<IReadOnlySet<string>> PermisosAsync(
        string usuario, string ruc, CancellationToken cancellationToken = default);

    /// <summary>Atajo: ¿el usuario tiene ese permiso en esa empresa?</summary>
    async Task<bool> TienePermisoAsync(
        string usuario, string ruc, string codigoPermiso, CancellationToken cancellationToken = default)
        => (await PermisosAsync(usuario, ruc, cancellationToken)).Contains(codigoPermiso);

    /// <summary>Contexto completo del usuario para una empresa.</summary>
    async Task<ContextoDeUsuario> ContextoAsync(
        string usuario, string ruc, CancellationToken cancellationToken = default)
        => new(usuario, ruc, await PermisosAsync(usuario, ruc, cancellationToken));
}
