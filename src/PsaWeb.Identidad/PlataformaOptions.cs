namespace PsaWeb.Identidad;

/// <summary>Configuración de la plataforma web (sección <c>Plataforma</c>).</summary>
public sealed class PlataformaOptions
{
    public const string SectionName = "Plataforma";

    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Usuarios (por <c>UserName</c>) con acceso a la administración web
    /// (<c>/admin/*</c>): alta de logins, reseteo de clave, auditoría. La
    /// asignación de empresas/roles sigue en la administración de PeachEBills.
    /// </summary>
    public List<string> Admins { get; set; } = new();

    public bool EsAdmin(string? usuario) =>
        !string.IsNullOrWhiteSpace(usuario)
        && Admins.Any(a => string.Equals(a, usuario, StringComparison.OrdinalIgnoreCase));
}
