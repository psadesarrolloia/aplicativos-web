using PsaWeb.Seguridad;

namespace PsaWeb.Ventas.Tests;

/// <summary>Directorio de seguridad falso: permisos por usuario (la empresa no importa).</summary>
internal sealed class DirectorioFalso(Dictionary<string, string[]> permisosPorUsuario) : ISecurityDirectory
{
    public Task<IReadOnlyList<EmpresaDelUsuario>> EmpresasDelUsuarioAsync(string usuario, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EmpresaDelUsuario>>(Array.Empty<EmpresaDelUsuario>());

    public Task<IReadOnlyDictionary<string, int>> ContarEmpresasAsync(IEnumerable<string> usuarios, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());

    public Task<IReadOnlySet<string>> PermisosAsync(string usuario, string ruc, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(permisosPorUsuario.GetValueOrDefault(usuario) ?? Array.Empty<string>()));

    public Task<string?> EmailUsuarioAsync(string usuario, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

    public Task<IReadOnlyList<string>> EmailsPorRolAsync(string ruc, string rol = "Supervisor", CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}
