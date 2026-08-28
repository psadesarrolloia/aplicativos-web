namespace PsaWeb.Sage50;

/// <summary>
/// Configuración de conexión a la base de Sage 50 (Pervasive / Actian Zen vía ODBC).
/// </summary>
public sealed class SageOptions
{
    public const string SectionName = "Sage50";

    /// <summary>
    /// Cadena de conexión ODBC. En desarrollo apunta al DSN de ejemplo (<c>DSN=demodata;</c>);
    /// en producción, a la base real. Nunca se guarda en el código: viene de configuración
    /// o de variables de entorno.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
