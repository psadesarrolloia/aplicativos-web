using System.Data.Odbc;
using PsaWeb.Ats.Esquema;

namespace PsaWeb.Ats.Anulados;

/// <summary>
/// Lee de Sage 50 los comprobantes anulados del período. Port de
/// <c>ATSModel.LoadCanceled</c> (<c>ATSfromPeach</c>).
/// </summary>
/// <remarks>
/// <b>Bug real preservado</b>: todo el procesamiento (incluidas las facturas y
/// notas de crédito de <b>ventas</b> canceladas) solo corre si hay al menos un
/// comprobante de <b>compra</b> cancelado (diario "PurchaseReceiveInventory").
/// Si en el período no se anuló ninguna compra, <see cref="LeerAsync"/>
/// devuelve una lista vacía aunque sí haya ventas o retenciones anuladas — así
/// es el `.exe` (la condición que arma la consulta de órdenes de compra
/// vinculadas envuelve también el bucle de clasificación). Verificado con el
/// golden real de CPTDC julio/2026: sus 2 anulados (ambos ventas) solo se
/// reportan porque hay, además, 1 compra cancelada en el mismo mes que no
/// aporta ninguna fila propia — sin esa compra, el `.exe` reportaría 0
/// anulados pese a las 2 ventas ANULAD%.
/// </remarks>
public static class LectorAnuladosAts
{
    private const string SqlAnulados = """
        SELECT JrnlHdr.PostOrder AS PostOrder, JrnlHdr.Reference AS Reference, JrnlHdr.CustVendId AS CustVendId,
               JrnlHdr.JournalEx AS JournalEx, JrnlHdr.JrnlKey_Journal AS JrnlKeyJournal,
               JrnlHdr.INV_POSOOrderNumber AS InvPosoOrderNumber, JrnlHdr.ShipVia AS ShipVia,
               JrnlHdr.TermsDescription AS TermsDescription, JrnlHdr.ShipToAddress2 AS ShipToAddress2
        FROM JrnlHdr
        WHERE (JrnlHdr.Description LIKE 'ANULAD%' OR JrnlHdr.Description LIKE 'RETENCION ANULAD%')
          AND JrnlHdr.Reference LIKE '___-___-%'
          AND "YEAR"(JrnlHdr.TransactionDate) = ?
          AND "MONTH"(JrnlHdr.TransactionDate) = ?
        """;

    public static async Task<IReadOnlyList<detalleAnuladosType>> LeerAsync(
        OdbcConnection connection, int periodo, int mes, CancellationToken cancellationToken = default)
    {
        var filas = await LeerFilasAnuladasAsync(connection, periodo, mes, cancellationToken);

        // Basta con que exista al menos una compra cancelada (JrnlKeyJournal=4,
        // JournalEx=11) para "destrabar" el resto — el `.exe` arma el filtro
        // con su INV_POSOOrderNumber tal cual, aunque venga vacío (una fila
        // con Reference='' igual deja el filtro no-vacío). Es justo el bug de
        // arriba: no exige INV_POSOOrderNumber no vacío para destrabar, solo
        // para el propio match de compraOriginal en ClasificadorAnuladosAts.
        var referenciasCompra = filas
            .Where(f => f.JrnlKeyJournal == 4 && f.JournalEx == 11)
            .Select(f => (f.InvPosoOrderNumber, f.CustVendId))
            .Distinct()
            .ToList();

        if (referenciasCompra.Count == 0)
        {
            return Array.Empty<detalleAnuladosType>();
        }

        var ordenesDeCompra = await LeerOrdenesDeCompraAsync(connection, referenciasCompra, cancellationToken);

        var postOrders = filas.Select(f => f.PostOrder)
            .Concat(ordenesDeCompra.Select(o => o.PostOrder))
            .Distinct()
            .ToList();
        var autorizaciones = await LeerAutorizacionesAsync(connection, postOrders, cancellationToken);

        var resultado = new List<detalleAnuladosType>();
        foreach (var fila in filas)
        {
            resultado.AddRange(ClasificadorAnuladosAts.Clasificar(fila, ordenesDeCompra, autorizaciones));
        }

        return resultado;
    }

    private static async Task<List<FilaJrnlHdrAts>> LeerFilasAnuladasAsync(
        OdbcConnection connection, int periodo, int mes, CancellationToken cancellationToken)
    {
        await using var cmd = new OdbcCommand(SqlAnulados, connection);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = periodo });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = mes });

        var resultado = new List<FilaJrnlHdrAts>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            resultado.Add(LeerFila(r));
        }

        return resultado;
    }

    private static async Task<List<FilaJrnlHdrAts>> LeerOrdenesDeCompraAsync(
        OdbcConnection connection, List<(string Reference, long VendorId)> referencias, CancellationToken cancellationToken)
    {
        var condiciones = string.Join(" OR ", referencias.Select(_ => "(Reference = ? AND CustVendId = ?)"));
        var sql = $"""
            SELECT JrnlHdr.PostOrder AS PostOrder, JrnlHdr.Reference AS Reference, JrnlHdr.CustVendId AS CustVendId,
                   JrnlHdr.JournalEx AS JournalEx, JrnlHdr.JrnlKey_Journal AS JrnlKeyJournal,
                   JrnlHdr.INV_POSOOrderNumber AS InvPosoOrderNumber, JrnlHdr.ShipVia AS ShipVia,
                   JrnlHdr.TermsDescription AS TermsDescription, JrnlHdr.ShipToAddress2 AS ShipToAddress2
            FROM JrnlHdr
            WHERE JrnlHdr.JournalEx = 18 AND JrnlHdr.JrnlKey_Journal = 10
              AND ({condiciones})
            """;

        await using var cmd = new OdbcCommand(sql, connection);
        foreach (var (reference, vendorId) in referencias)
        {
            cmd.Parameters.Add(new OdbcParameter { Value = reference });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = vendorId });
        }

        var resultado = new List<FilaJrnlHdrAts>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            resultado.Add(LeerFila(r));
        }

        return resultado;
    }

    private static async Task<Dictionary<long, string>> LeerAutorizacionesAsync(
        OdbcConnection connection, List<long> postOrders, CancellationToken cancellationToken)
    {
        var resultado = new Dictionary<long, string>();

        // En lotes por si el período tiene muchos comprobantes anulados (mismo
        // criterio que otros módulos de esta migración para listas IN largas).
        foreach (var lote in postOrders.Chunk(200))
        {
            var placeholders = string.Join(", ", lote.Select(_ => "?"));
            var sql = $"""
                SELECT JrnlRow.PostOrder AS PostOrder, JrnlRow.RowDescription AS Autorizacion
                FROM JrnlRow, LineItem
                WHERE JrnlRow.ItemRecordNumber = LineItem.ItemRecordNumber
                  AND LineItem.ItemID = 'AUT-SRI'
                  AND JrnlRow.PostOrder IN ({placeholders})
                """;

            await using var cmd = new OdbcCommand(sql, connection);
            foreach (var postOrder in lote)
            {
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = postOrder });
            }

            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                var postOrder = Convert.ToInt64(r.GetValue(r.GetOrdinal("PostOrder")));
                if (!resultado.ContainsKey(postOrder)) // primero que aparece gana, como el FirstOrDefault original.
                {
                    resultado[postOrder] = r.GetValue(r.GetOrdinal("Autorizacion"))?.ToString() ?? string.Empty;
                }
            }
        }

        return resultado;
    }

    private static FilaJrnlHdrAts LeerFila(System.Data.Common.DbDataReader r) => new(
        PostOrder: Convert.ToInt64(r.GetValue(r.GetOrdinal("PostOrder"))),
        Reference: r.GetValue(r.GetOrdinal("Reference")).ToString() ?? string.Empty,
        CustVendId: Convert.ToInt64(r.GetValue(r.GetOrdinal("CustVendId"))),
        JournalEx: Convert.ToInt32(r.GetValue(r.GetOrdinal("JournalEx"))),
        JrnlKeyJournal: Convert.ToInt32(r.GetValue(r.GetOrdinal("JrnlKeyJournal"))),
        InvPosoOrderNumber: r.IsDBNull(r.GetOrdinal("InvPosoOrderNumber")) ? string.Empty : r.GetValue(r.GetOrdinal("InvPosoOrderNumber")).ToString() ?? string.Empty,
        ShipVia: r.IsDBNull(r.GetOrdinal("ShipVia")) ? string.Empty : r.GetValue(r.GetOrdinal("ShipVia")).ToString() ?? string.Empty,
        TermsDescription: r.IsDBNull(r.GetOrdinal("TermsDescription")) ? string.Empty : r.GetValue(r.GetOrdinal("TermsDescription")).ToString() ?? string.Empty,
        ShipToAddress2: r.IsDBNull(r.GetOrdinal("ShipToAddress2")) ? string.Empty : r.GetValue(r.GetOrdinal("ShipToAddress2")).ToString() ?? string.Empty);
}
