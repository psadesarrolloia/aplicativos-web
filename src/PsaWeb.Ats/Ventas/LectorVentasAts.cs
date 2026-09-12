using System.Data.Odbc;
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Ats.Ventas;

/// <summary>
/// Lee de Sage 50 las ventas del período agrupadas por cliente + tipo de
/// comprobante. Port de la consulta principal de <c>LoadSales</c> — arma una
/// <see cref="FilaVentaCruda"/> por grupo, resolviendo el cliente
/// (<see cref="LectorClienteAts"/>) y los montos
/// (<see cref="LectorDetalleVentaAts"/>) de cada uno.
/// </summary>
public static class LectorVentasAts
{
    private static readonly string Sql = $"""
        SELECT JrnlHdr.CustVendId AS CustomerId, JrnlHdr.JournalEx AS JournalEx,
               COUNT(DISTINCT JrnlHdr.PostOrder) AS Cuenta
        FROM JrnlHdr, Customers
        WHERE JrnlHdr.CustVendId = Customers.CustomerRecordNumber
          AND JrnlHdr.JrnlKey_Journal = {DiarioSage.Ventas}
          AND NOT (Customers.Customer_Bill_Name LIKE 'ANULAD%')
          AND "MONTH"(JrnlHdr.TransactionDate) = ?
          AND "YEAR"(JrnlHdr.TransactionDate) = ?
          AND NOT (JrnlHdr.PurchOrder LIKE '4R')
          AND JrnlHdr.Reference LIKE '___-___-%'
        GROUP BY JrnlHdr.CustVendId, JrnlHdr.JournalEx
        """;

    /// <summary>
    /// Lee las filas de ventas del período. Los grupos cuyo <c>JournalEx</c>
    /// no es factura (8) ni nota de crédito (9) se descartan — en el diario de
    /// ventas (<c>JrnlKey_Journal = 3</c>) de Sage/Peach solo existen esos dos
    /// subtipos, así que en la práctica esto nunca filtra nada (a diferencia
    /// del `.exe`, que los agregaba igual con los campos en blanco).
    /// </summary>
    public static async Task<IReadOnlyList<FilaVentaCruda>> LeerAsync(
        OdbcConnection connection, int periodo, int mes, CancellationToken cancellationToken = default)
    {
        var grupos = new List<(long CustomerId, int JournalEx, string Cuenta)>();

        await using (var cmd = new OdbcCommand(Sql, connection))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = mes });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = periodo });

            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                var customerId = Convert.ToInt64(r.GetValue(r.GetOrdinal("CustomerId")));
                var journalEx = Convert.ToInt32(r.GetValue(r.GetOrdinal("JournalEx")));
                var cuenta = r.GetValue(r.GetOrdinal("Cuenta")).ToString() ?? "0";
                grupos.Add((customerId, journalEx, cuenta));
            }
        }

        var filas = new List<FilaVentaCruda>(grupos.Count);
        foreach (var (customerId, journalEx, cuenta) in grupos)
        {
            string tipoComprobante;
            if (journalEx == DiarioSage.JournalExFacturaVenta)
            {
                tipoComprobante = "18";
            }
            else if (journalEx == DiarioSage.JournalExNotaCreditoVenta)
            {
                tipoComprobante = "04";
            }
            else
            {
                continue;
            }

            var cliente = await LectorClienteAts.LeerAsync(connection, customerId, cancellationToken);
            var buckets = await LectorDetalleVentaAts.LeerAsync(
                connection,
                customerId,
                journalEx,
                periodo,
                mes,
                incluirRetencionRecibida: tipoComprobante == "18",
                cancellationToken);

            filas.Add(new FilaVentaCruda(tipoComprobante, cuenta, cliente, buckets));
        }

        return filas;
    }
}
