using System.Data.Odbc;
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Comprobantes.Retenciones;

/// <summary>Una factura de compra de Sage 50 candidata a retención (para el listado por fechas).</summary>
public sealed record CompraPendiente(string PostOrder, string NumeroReferencia, DateTime Fecha);

/// <summary>
/// Lista las facturas de compra de Sage 50 en un rango de fechas. La lista de
/// <em>pendientes</em> de retención sale de <c>PurchaseOrderSync</c> (SQL, sin
/// fecha); este lector aporta la fecha y el número de cada compra para poder
/// acotar por rango y mostrarlas igual que los otros tipos de comprobante.
/// </summary>
public static class LectorComprasPendientes
{
    private static readonly string Sql = $"""
        SELECT JrnlHdr.Reference, JrnlHdr.PostOrder, JrnlHdr.TransactionDate
        FROM JrnlHdr
        WHERE JrnlHdr.JrnlKey_Journal = {DiarioSage.Compras}
          AND NOT (JrnlHdr.Description LIKE 'ANULAD%')
          AND (JrnlHdr.TransactionDate BETWEEN ? AND ?)
        ORDER BY JrnlHdr.TransactionDate, JrnlHdr.PostOrder
        """;

    public static async Task<IReadOnlyList<CompraPendiente>> ListarAsync(
        OdbcConnection conexion, DateTime desde, DateTime hasta, CancellationToken cancellationToken = default)
    {
        await using var cmd = new OdbcCommand(Sql, conexion);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = desde.Date });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = hasta.Date });

        var lista = new List<CompraPendiente>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lista.Add(new CompraPendiente(
                PostOrder: reader.IsDBNull(1) ? string.Empty : reader.GetValue(1)?.ToString() ?? string.Empty,
                NumeroReferencia: reader.IsDBNull(0) ? string.Empty : reader.GetValue(0)?.ToString() ?? string.Empty,
                Fecha: reader.IsDBNull(2) ? default : Convert.ToDateTime(reader.GetValue(2))));
        }
        return lista;
    }
}
