namespace PsaWeb.PeachEbills;

/// <summary>
/// Configuración de conexión a la base SQL Server compartida <c>PeachEBills</c>
/// (config multi-empresa, seguimiento de documentos, config de Datil).
/// </summary>
public sealed class PeachEbillsOptions
{
    public const string SectionName = "PeachEbills";

    /// <summary>
    /// Cadena de conexión SQL Server. En desarrollo apunta a la copia local
    /// (<c>Server=.\SQLEXPRESS;Database=PeachEBills;Trusted_Connection=True;TrustServerCertificate=True</c>);
    /// en producción, a la base real de <c>SERWEBPSA01</c> / <c>192.168.0.11</c>.
    /// Nunca se guarda en el código.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Solo desarrollo: reemplaza el <c>servername</c> de <b>toda</b> cadena ODBC de Sage 50
    /// resuelta desde <c>PeachConnString</c>. Las filas apuntan a <c>SERWEBPSA01</c>, que no
    /// se alcanza desde PREDATOR; poner <c>localhost</c> para trabajar contra las compañías
    /// Sage locales. Vacío = usar el <c>servername</c> de la fila tal cual.
    /// </summary>
    public string? SageServerNameOverride { get; set; }

    /// <summary>
    /// Solo desarrollo: <c>"RUC=dbq"</c> para apuntar un RUC a otra compañía (p. ej. la copia de prueba de la Ola 2),
    /// igual que <c>BasesPorRuc</c> del Sage Bridge: así la web lee la misma base en la que el Bridge escribe.
    /// </summary>
    public string[]? DbqPorRuc { get; set; }
}
