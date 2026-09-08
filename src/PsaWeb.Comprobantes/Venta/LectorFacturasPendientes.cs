using System.Data.Odbc;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>Una factura de venta pendiente de generar como comprobante electrónico.</summary>
public sealed record FacturaPendiente(string PostOrder, string NumeroReferencia, DateTime Fecha);

/// <summary>
/// Lista las facturas de venta de Sage 50 en un rango de fechas (para elegir cuál
/// generar). Port de la consulta de <c>LoadSaleInvoices</c>. El detalle de cada
/// una se carga aparte con <see cref="LectorFacturaVenta.LeerAsync"/>.
/// </summary>
public static class LectorFacturasPendientes
{
    private const string Sql = """
        SELECT JrnlHdr.Reference, JrnlHdr.PostOrder, JrnlHdr.TransactionDate
        FROM { oj JrnlHdr LEFT OUTER JOIN Tax_Code ON JrnlHdr.SalesTaxCode = Tax_Code.ID }
        WHERE (JrnlHdr.JrnlKey_Journal = 3)
          AND (NOT (JrnlHdr.Description LIKE 'ANULAD%'))
          AND (JrnlHdr.JournalEx = 8) AND (JrnlHdr.JrnlTypeEx = 0)
          AND (JrnlHdr.TransactionDate BETWEEN ? AND ?)
        ORDER BY JrnlHdr.TransactionDate, JrnlHdr.PostOrder
        """;

    public static async Task<IReadOnlyList<FacturaPendiente>> ListarAsync(
        OdbcConnection conexion, DateTime desde, DateTime hasta, CancellationToken cancellationToken = default)
    {
        await using var cmd = new OdbcCommand(Sql, conexion);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = desde.Date });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = hasta.Date });

        var lista = new List<FacturaPendiente>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lista.Add(new FacturaPendiente(
                PostOrder: reader.IsDBNull(1) ? string.Empty : reader.GetValue(1)?.ToString() ?? string.Empty,
                NumeroReferencia: reader.IsDBNull(0) ? string.Empty : reader.GetValue(0)?.ToString() ?? string.Empty,
                Fecha: reader.IsDBNull(2) ? default : Convert.ToDateTime(reader.GetValue(2))));
        }
        return lista;
    }
}
