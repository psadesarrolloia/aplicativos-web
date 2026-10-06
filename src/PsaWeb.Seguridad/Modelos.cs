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

/// <summary>
/// Vínculo de la cuenta web con su usuario de Sage 50 en una empresa (PLAN-ACCESOS-WEB §6). <see cref="Exigido"/> = false cuando la fuente
/// de accesos no lo maneja (PeachEBills): ahí no se bloquea nada. Con la fuente Web, sin usuario de Sage no se escribe en Sage.
/// </summary>
public sealed record VinculoSage(bool Exigido, string? UsuarioSage)
{
    public static readonly VinculoSage NoAplica = new(false, null);

    public bool PermiteEscribir => !Exigido || !string.IsNullOrWhiteSpace(UsuarioSage);
}
