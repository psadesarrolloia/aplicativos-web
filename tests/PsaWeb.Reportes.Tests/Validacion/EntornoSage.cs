using System.Data.Odbc;
using PsaWeb.Modules.Reportes.Comun;
using PsaWeb.Sage50;

namespace PsaWeb.Reportes.Tests.Validacion;

/// <summary>
/// Entorno de las pruebas de validación contra el Sage 50 real de Radio FM Efemedio (PREDATOR). Todo es
/// opcional: sin las variables de entorno las pruebas se saltan y el resto de la suite corre igual.
/// <list type="bullet">
///   <item><c>PSAWEB_TEST_SAGE_EFEMEDIO</c> — cadena ODBC de Efemedio (lleva la clave; NUNCA se commitea).</item>
///   <item><c>PSAWEB_TEST_PWC_XLSX</c> / <c>PSAWEB_TEST_COMISIONES_XLSX</c> — Excel de referencia del usuario
///   (datos de un cliente; tampoco se commitean). Por defecto se buscan en Descargas.</item>
/// </list>
/// Las pruebas corren a 32 bits porque el driver ODBC de Pervasive es de 32 bits.
/// </summary>
internal static class EntornoSage
{
    internal static string? CadenaEfemedio => Environment.GetEnvironmentVariable("PSAWEB_TEST_SAGE_EFEMEDIO");

    internal static string RutaExcelPwc => Environment.GetEnvironmentVariable("PSAWEB_TEST_PWC_XLSX")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CXC PWC EFEMEDIO.xlsx");

    internal static string RutaExcelComisiones => Environment.GetEnvironmentVariable("PSAWEB_TEST_COMISIONES_XLSX")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "COMISIONES EFEMEDIO.xlsx");

    internal static SageAcceso Acceso(string cadena)
        => new(new FabricaFija(cadena), new SinShellResolverEmpresaSage());

    private sealed class FabricaFija(string cadena) : ISageConnectionFactory
    {
        public OdbcConnection CreateConnection() => new(cadena);
        public OdbcConnection CreateConnection(string connectionString) => new(connectionString);
    }
}
