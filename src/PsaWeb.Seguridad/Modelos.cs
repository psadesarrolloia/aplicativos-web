namespace PsaWeb.Seguridad;

/// <summary>Una empresa a la que el usuario tiene acceso.</summary>
public sealed record EmpresaDelUsuario(string Ruc, string Nombre, int Orden);

/// <summary>
/// Contexto de seguridad del usuario para una empresa: los códigos de permiso
/// que tiene y las apps web que puede ver.
/// </summary>
public sealed record ContextoDeUsuario(
    string Usuario,
    string Ruc,
    IReadOnlySet<string> Permisos)
{
    public bool Puede(string codigoPermiso) => Permisos.Contains(codigoPermiso);
}
