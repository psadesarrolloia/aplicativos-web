namespace PsaWeb.Seguridad;

/// <summary>Usuarios de Sage 50 conocidos en una compañía, o el motivo por el que no se pudieron leer.</summary>
public sealed record UsuariosSage(IReadOnlyList<string> Usuarios, string? Error)
{
    public bool Contiene(string? usuario) =>
        !string.IsNullOrWhiteSpace(usuario) && Usuarios.Any(u => string.Equals(u, usuario.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Lista de usuarios de Sage 50 de una compañía (PLAN-ACCESOS-WEB §6, AW-5). Sage no expone su tabla de usuarios por ODBC; se usa
/// <c>UserPreference.UserID</c>: todo usuario que entró alguna vez a esa compañía (verificado contra SANCEV en PREDATOR, 2026-10-06).
/// Sirve para sugerir y validar el usuario de Sage en el panel; un usuario que nunca entró no aparece y se puede cargar a mano.
/// </summary>
public interface ILectorUsuariosSage
{
    Task<UsuariosSage> UsuariosAsync(string ruc, CancellationToken cancellationToken = default);
}
