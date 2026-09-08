using System.Data.Odbc;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>Una nota de crédito de venta pendiente de generar como comprobante electrónico.</summary>
public sealed record NotaCreditoPendiente(string PostOrder, string NumeroReferencia, DateTime Fecha);

/// <summary>
/// Lista las notas de crédito de venta de Sage 50 en un rango de fechas. Port de
/// la consulta de <c>LoadSaleNCs</c> (<c>JrnlKey_Journal=3</c>, <c>JrnlTypeEx=2</c>,
/// <c>JournalEx=9</c>, sin <c>ANULAD%</c>, sin <c>PurchOrder LIKE '4R'</c>).
/// El detalle se carga aparte con <see cref="LectorNotaCredito.LeerAsync"/>.
/// </summary>
public static class LectorNotasCreditoPendientes
{
    private const string Sql = """
        SELECT JrnlHdr.PostOrder, JrnlHdr.Reference, JrnlHdr.TransactionDate
        FROM { oj JrnlHdr LEFT OUTER JOIN Tax_Code ON JrnlHdr.SalesTaxCode = Tax_Code.ID }
        WHERE (JrnlHdr.JrnlKey_Journal = 3) AND (JrnlHdr.JrnlTypeEx = 2)
          AND (NOT (JrnlHdr.Description LIKE 'ANULAD%')) AND (NOT (JrnlHdr.PurchOrder LIKE '4R'))
          AND (JrnlHdr.JournalEx = 9)
          AND (JrnlHdr.TransactionDate BETWEEN ? AND ?)
        ORDER BY JrnlHdr.TransactionDate, JrnlHdr.PostOrder
        """;

    public static async Task<IReadOnlyList<NotaCreditoPendiente>> ListarAsync(
        OdbcConnection conexion, DateTime desde, DateTime hasta, CancellationToken cancellationToken = default)
    {
        await using var cmd = new OdbcCommand(Sql, conexion);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = desde.Date });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = hasta.Date });

        var lista = new List<NotaCreditoPendiente>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lista.Add(new NotaCreditoPendiente(
                PostOrder: reader.IsDBNull(0) ? string.Empty : reader.GetValue(0)?.ToString() ?? string.Empty,
                NumeroReferencia: reader.IsDBNull(1) ? string.Empty : reader.GetValue(1)?.ToString() ?? string.Empty,
                Fecha: reader.IsDBNull(2) ? default : Convert.ToDateTime(reader.GetValue(2))));
        }
        return lista;
    }
}
