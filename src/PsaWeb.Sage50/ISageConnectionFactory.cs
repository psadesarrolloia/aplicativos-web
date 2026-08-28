using System.Data.Odbc;

namespace PsaWeb.Sage50;

/// <summary>
/// Crea conexiones ODBC a la base de Sage 50. Quien la consume es responsable de
/// abrir, usar y liberar la conexión (normalmente con <c>using</c>).
/// </summary>
public interface ISageConnectionFactory
{
    OdbcConnection CreateConnection();
}
