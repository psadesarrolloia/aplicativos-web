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
}
