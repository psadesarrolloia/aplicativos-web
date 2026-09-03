using System.Data.Odbc;

namespace PsaWeb.Sage50;

/// <summary>
/// Crea conexiones ODBC a Sage 50. Quien la consume es responsable de abrir,
/// usar y liberar la conexión (normalmente con <c>using</c>).
/// </summary>
public interface ISageConnectionFactory
{
    /// <summary>Conexión a la empresa configurada en <c>Sage50:ConnectionString</c> (mono-empresa).</summary>
    OdbcConnection CreateConnection();

    /// <summary>
    /// Conexión con una cadena explícita (multi-empresa: la arma
    /// <c>PeachConnStringResolver</c> a partir del RUC).
    /// </summary>
    OdbcConnection CreateConnection(string connectionString);
}
