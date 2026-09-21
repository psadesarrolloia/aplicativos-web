using System.Data.Odbc;
using System.Globalization;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.Reportes.Comun;

/// <summary>
/// Conexión a la empresa de Sage 50 que corresponda (RUC de sesión con shell, cadena de
/// configuración sin shell) y utilidades ODBC comunes a los 3 repositorios. Todo con
/// <see cref="OdbcParameter"/> posicionales: nunca se interpolan valores en el SQL.
/// </summary>
internal sealed class SageAcceso(ISageConnectionFactory connections, IResolverEmpresaSage resolver)
{
    /// <summary>Tamaño de lote de los <c>IN (?, ?, …)</c> (el .exe hacía una consulta por fila).</summary>
    internal const int LoteIn = 200;

    internal string? RucSesion => resolver.RucSesion;

    /// <summary>Abre la conexión de la empresa de sesión.</summary>
    internal async Task<OdbcConnection> AbrirSesionAsync(CancellationToken ct)
        => await AbrirAsync(RucSesion is { } ruc ? await resolver.CadenaOdbcAsync(ruc, ct) : null, ct);

    /// <summary>Abre la conexión de una empresa explícita (endpoints de exportación, sin circuito).</summary>
    internal async Task<OdbcConnection> AbrirParaRucAsync(string ruc, CancellationToken ct)
        => await AbrirAsync(await resolver.CadenaOdbcAsync(ruc, ct), ct);

    private async Task<OdbcConnection> AbrirAsync(string? cadena, CancellationToken ct)
    {
        var cn = cadena is null ? connections.CreateConnection() : connections.CreateConnection(cadena);
        await cn.OpenAsync(ct);
        return cn;
    }

    internal static string Marcadores(int n) => string.Join(", ", Enumerable.Repeat("?", n));

    internal static IEnumerable<IReadOnlyList<T>> Lotes<T>(IReadOnlyList<T> origen, int tamano = LoteIn)
    {
        for (var i = 0; i < origen.Count; i += tamano)
        {
            yield return origen.Skip(i).Take(tamano).ToList();
        }
    }

    internal static void AgregarFecha(OdbcCommand cmd, DateTime valor)
        => cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = valor.Date });

    /// <summary>PostOrder / LinkToAnotherTrx son enteros en Pervasive (el .exe los lee como Int32).</summary>
    internal static void AgregarEnteros(OdbcCommand cmd, IEnumerable<long> valores)
    {
        foreach (var v in valores)
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = (int)v });
        }
    }

    internal static string Texto(System.Data.Common.DbDataReader r, int i)
        => r.IsDBNull(i) ? "" : (Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture) ?? "").Trim();

    internal static decimal? Decimal(System.Data.Common.DbDataReader r, int i)
        => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i), CultureInfo.InvariantCulture);

    internal static DateTime? Fecha(System.Data.Common.DbDataReader r, int i)
        => r.IsDBNull(i) ? null : Convert.ToDateTime(r.GetValue(i), CultureInfo.InvariantCulture);
}
