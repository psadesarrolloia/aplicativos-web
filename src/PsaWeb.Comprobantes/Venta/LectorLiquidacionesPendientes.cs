using System.Data.Odbc;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>Una liquidación de compra pendiente de generar como comprobante electrónico.</summary>
public sealed record LiquidacionPendiente(string PostOrder, string NumeroReferencia, DateTime Fecha);

/// <summary>
/// Lista las liquidaciones de compra de Sage 50 en un rango de fechas. Port de la
/// consulta de <c>LoadPurchaseLiqs</c> (<c>JrnlKey_Journal=4</c>, <c>JournalEx=11</c>,
/// <c>JrnlTypeEx=0</c>, <c>ShipVia LIKE 'LIQUIDACION'</c>). El detalle se carga
/// aparte con <see cref="LectorLiquidacionCompra.LeerAsync"/>.
/// </summary>
public static class LectorLiquidacionesPendientes
{
    private const string Sql = """
        SELECT JrnlHdr.PostOrder, JrnlHdr.Reference, JrnlHdr.TransactionDate
        FROM JrnlHdr
        WHERE (JrnlHdr.JrnlKey_Journal = 4)
          AND (NOT (JrnlHdr.Description LIKE 'ANULAD%'))
          AND (JrnlHdr.JournalEx = 11) AND (JrnlHdr.JrnlTypeEx = 0)
          AND (JrnlHdr.TransactionDate BETWEEN ? AND ?)
          AND (JrnlHdr.ShipVia LIKE 'LIQUIDACION')
        ORDER BY JrnlHdr.TransactionDate, JrnlHdr.PostOrder
        """;

    public static async Task<IReadOnlyList<LiquidacionPendiente>> ListarAsync(
        OdbcConnection conexion, DateTime desde, DateTime hasta, CancellationToken cancellationToken = default)
    {
        await using var cmd = new OdbcCommand(Sql, conexion);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = desde.Date });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = hasta.Date });

        var lista = new List<LiquidacionPendiente>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lista.Add(new LiquidacionPendiente(
                PostOrder: reader.IsDBNull(0) ? string.Empty : reader.GetValue(0)?.ToString() ?? string.Empty,
                NumeroReferencia: reader.IsDBNull(1) ? string.Empty : reader.GetValue(1)?.ToString() ?? string.Empty,
                Fecha: reader.IsDBNull(2) ? default : Convert.ToDateTime(reader.GetValue(2))));
        }
        return lista;
    }
}
