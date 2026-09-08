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

    /// <summary>
    /// Cantidad de empresas asignadas a cada usuario, en una sola consulta
    /// (para listados de administración).
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> ContarEmpresasAsync(
        IEnumerable<string> usuarios, CancellationToken cancellationToken = default);

    /// <summary>Códigos de permiso activos del usuario en una empresa.</summary>
    Task<IReadOnlySet<string>> PermisosAsync(
        string usuario, string ruc, CancellationToken cancellationToken = default);

    /// <summary>Email registrado de un usuario (tabla <c>user</c>). Port de <c>FEAllowed.userEmail</c>.</summary>
    Task<string?> EmailUsuarioAsync(
        string usuario, CancellationToken cancellationToken = default);

    /// <summary>
    /// Emails de los usuarios que tienen un rol (por nombre) en una empresa.
    /// Port de <c>FEAllowed.emailsByRole</c> (default rol "Supervisor").
    /// </summary>
    Task<IReadOnlyList<string>> EmailsPorRolAsync(
        string ruc, string rol = "Supervisor", CancellationToken cancellationToken = default);

    /// <summary>Atajo: ¿el usuario tiene ese permiso en esa empresa?</summary>
    async Task<bool> TienePermisoAsync(
        string usuario, string ruc, string codigoPermiso, CancellationToken cancellationToken = default)
        => (await PermisosAsync(usuario, ruc, cancellationToken)).Contains(codigoPermiso);

    /// <summary>Contexto completo del usuario para una empresa.</summary>
    async Task<ContextoDeUsuario> ContextoAsync(
        string usuario, string ruc, CancellationToken cancellationToken = default)
        => new(usuario, ruc, await PermisosAsync(usuario, ruc, cancellationToken));
}
