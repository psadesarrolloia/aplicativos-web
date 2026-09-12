using System.Data.Odbc;
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Ats.Ventas;

/// <summary>
/// Lee de Sage 50 los montos de un cliente + tipo de comprobante de ventas
/// (los 4 <c>Load*</c> + la retención recibida de <c>LoadSales</c>, port de
/// <c>ATSfromPeach</c>). Las 3 primeras consultas comparten forma — solo
/// cambia el predicado de <c>SalesTaxType</c> — así que se arman con
/// <see cref="Consulta"/> en vez de triplicar el texto.
/// </summary>
public static class LectorDetalleVentaAts
{
    // Las 2 apariciones de JournalEx en el OR usan el mismo valor (el `.exe`
    // repite la misma condición con JrnlTypeEx 0 y 2) — quedan como 2
    // parámetros porque ODBC no permite reusar un "?" posicional.
    private static string Consulta(string predicadoTax) => $"""
        SELECT SUM(JrnlRow.Amount) AS Imponible
        FROM JrnlHdr, JrnlRow
        WHERE JrnlHdr.CustVendId = ?
          AND JrnlHdr.PurchOrder <> '4R'
          AND JrnlHdr.PostOrder = JrnlRow.PostOrder
          AND JrnlHdr.JrnlKey_Journal = {DiarioSage.Ventas}
          AND (
                (JrnlHdr.JournalEx = ? AND JrnlHdr.JrnlTypeEx = 0 AND JrnlRow.RowNumber > 0)
             OR (JrnlHdr.JournalEx = ? AND JrnlHdr.JrnlTypeEx = 2 AND JrnlRow.RowNumber > 0)
          )
          AND JrnlRow.RowType = 0
          AND JrnlRow.Amount <> 0
          AND "MONTH"(JrnlHdr.TransactionDate) = ?
          AND "YEAR"(JrnlHdr.TransactionDate) = ?
          AND JrnlHdr.Reference LIKE '___-___-%'
          AND ({predicadoTax})
        """;

    // < baseNoGraIva >: no objeto de IVA.
    private static readonly string SqlBaseNoGraIva =
        Consulta("JrnlRow.SalesTaxType = 5 OR JrnlRow.SalesTaxType = 6");

    // Gravado 0%. Bug B1 (ver BucketsVentaAts): este valor nunca llega al XML.
    private static readonly string SqlBaseImponibleCruda =
        Consulta("JrnlRow.SalesTaxType <> 0 AND JrnlRow.SalesTaxType <> 5 AND JrnlRow.SalesTaxType <> 6");

    // < baseImpGrav >: gravado con tarifa.
    private static readonly string SqlBaseImpGrav =
        Consulta("JrnlRow.SalesTaxType = 0");

    // < montoIva >: usa JrnlRow.Journal (no JrnlHdr.JrnlKey_Journal) tal cual el `.exe`.
    private static readonly string SqlMontoIva = $"""
        SELECT SUM(JrnlRow.Amount) AS MontoIva
        FROM JrnlHdr, JrnlRow
        WHERE JrnlHdr.PostOrder = JrnlRow.PostOrder
          AND JrnlRow.Journal = {DiarioSage.Ventas}
          AND JrnlHdr.PurchOrder <> '4R'
          AND JrnlRow.RowType = 5
          AND JrnlHdr.JournalEx = ?
          AND "MONTH"(JrnlHdr.TransactionDate) = ?
          AND "YEAR"(JrnlHdr.TransactionDate) = ?
          AND JrnlHdr.Reference LIKE '___-___-%'
          AND JrnlHdr.CustVendId = ?
        """;

    // Retenciones que le hicieron al informante sobre esta venta: entradas
    // aparte con JournalEx=9 (fijo, no depende del tipo de comprobante) y
    // PurchOrder='4R' como marca de retención. Solo aplica a facturas.
    private static readonly string SqlRetencionRecibida = $"""
        SELECT SUM(JrnlRow.Amount) AS RetIva, Jobs.JobID AS JobId
        FROM JrnlRow, JrnlHdr, Jobs
        WHERE JrnlRow.PostOrder = JrnlHdr.PostOrder
          AND JrnlRow.JobRecordNumber = Jobs.JobRecordNumber
          AND JrnlHdr.JrnlKey_Journal = {DiarioSage.Ventas}
          AND JrnlHdr.JournalEx = {DiarioSage.JournalExNotaCreditoVenta}
          AND JrnlHdr.PurchOrder = '4R'
          AND JrnlHdr.Reference LIKE '___-___-%'
          AND "MONTH"(JrnlHdr.TransactionDate) = ?
          AND "YEAR"(JrnlHdr.TransactionDate) = ?
          AND JrnlHdr.CustVendId = ?
        GROUP BY Jobs.JobID
        HAVING (Jobs.JobID LIKE 'IRF' OR Jobs.JobID LIKE 'IVA')
        """;

    /// <param name="incluirRetencionRecibida">
    /// Solo las facturas (<c>tipoComprobante = "18"</c>) llevan retención
    /// recibida en el `.exe`; las NC no.
    /// </param>
    public static async Task<BucketsVentaAts> LeerAsync(
        OdbcConnection connection,
        long customerId,
        int journalEx,
        int periodo,
        int mes,
        bool incluirRetencionRecibida,
        CancellationToken cancellationToken = default)
    {
        var baseNoGraIva = await SumarAsync(connection, SqlBaseNoGraIva, customerId, journalEx, periodo, mes, cancellationToken);
        var baseImponibleCruda = await SumarAsync(connection, SqlBaseImponibleCruda, customerId, journalEx, periodo, mes, cancellationToken);
        var baseImpGrav = await SumarAsync(connection, SqlBaseImpGrav, customerId, journalEx, periodo, mes, cancellationToken);
        var montoIva = await SumarMontoIvaAsync(connection, customerId, journalEx, periodo, mes, cancellationToken);

        var valorRetIva = 0m;
        var valorRetRenta = 0m;
        if (incluirRetencionRecibida)
        {
            (valorRetIva, valorRetRenta) = await LeerRetencionRecibidaAsync(connection, customerId, periodo, mes, cancellationToken);
        }

        return new BucketsVentaAts(
            DecCorrectValue(baseNoGraIva),
            DecCorrectValue(baseImponibleCruda),
            DecCorrectValue(baseImpGrav),
            DecCorrectValue(montoIva),
            valorRetIva,
            valorRetRenta);
    }

    private static async Task<decimal> SumarAsync(
        OdbcConnection connection, string sql, long customerId, int journalEx, int periodo, int mes,
        CancellationToken cancellationToken)
    {
        await using var cmd = new OdbcCommand(sql, connection);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = customerId });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = journalEx });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = journalEx });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = mes });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = periodo });

        var valor = await cmd.ExecuteScalarAsync(cancellationToken);
        return valor is null or DBNull ? 0m : Convert.ToDecimal(valor);
    }

    private static async Task<decimal> SumarMontoIvaAsync(
        OdbcConnection connection, long customerId, int journalEx, int periodo, int mes,
        CancellationToken cancellationToken)
    {
        await using var cmd = new OdbcCommand(SqlMontoIva, connection);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = journalEx });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = mes });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = periodo });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = customerId });

        var valor = await cmd.ExecuteScalarAsync(cancellationToken);
        return valor is null or DBNull ? 0m : Convert.ToDecimal(valor);
    }

    private static async Task<(decimal ValorRetIva, decimal ValorRetRenta)> LeerRetencionRecibidaAsync(
        OdbcConnection connection, long customerId, int periodo, int mes, CancellationToken cancellationToken)
    {
        await using var cmd = new OdbcCommand(SqlRetencionRecibida, connection);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = mes });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = periodo });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = customerId });

        var valorRetIva = 0m;
        var valorRetRenta = 0m;

        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            var monto = Convert.ToDecimal(r.GetValue(r.GetOrdinal("RetIva")));
            var jobId = r.GetValue(r.GetOrdinal("JobId"))?.ToString();

            // El `.exe` compara con .Equals() sobre el object del reader (no
            // recorta espacios); se preserva igual.
            if (jobId == "IRF")
            {
                valorRetRenta = DecCorrectValue(monto);
            }
            if (jobId == "IVA")
            {
                valorRetIva = DecCorrectValue(monto);
            }
        }

        return (valorRetIva, valorRetRenta);
    }

    private static decimal DecCorrectValue(decimal valor) => Math.Round(Math.Abs(valor), 2);
}
