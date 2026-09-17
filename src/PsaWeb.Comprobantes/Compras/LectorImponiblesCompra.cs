using System.Data.Odbc;
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Comprobantes.Compras;

/// <summary>
/// Lee de Sage 50 las bases imponibles de una compra (port de
/// <c>LoadImponibles</c>, <c>ATSfromPeach</c>). Compartido entre el ATS y
/// Conciliación SRI (§13.1 del plan) — a diferencia de
/// <c>LectorDetalleComprasAts.LeerRetencionesRentaAsync</c> (que se queda en
/// <c>PsaWeb.Ats</c>, devuelve el tipo del esquema XML), esto no está acoplado
/// al ATS: da un desglose neutral de bases + IVA.
/// </summary>
public static class LectorImponiblesCompra
{
    private static readonly string SqlImponibles = $"""
        SELECT LineItem.Category, LineItem.CustomField1, LineItem.CustomField3, LineItem.CustomField4, JrnlRow.Amount, LineItem.LaborCost
        FROM JrnlRow, LineItem
        WHERE JrnlRow.ItemRecordNumber = LineItem.ItemRecordNumber
          AND JrnlRow.Journal = {DiarioSage.Compras}
          AND JrnlRow.RowType = 0
          AND JrnlRow.RowNumber > 0
          AND JrnlRow.PostOrder = ?
        ORDER BY JrnlRow.RowNumber
        """;

    public static async Task<BucketsCompra> LeerImponiblesAsync(
        OdbcConnection connection, long postOrder, CancellationToken cancellationToken = default)
    {
        await using var cmd = new OdbcCommand(SqlImponibles, connection);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = postOrder });

        var acumulado = BucketsCompra.Cero;
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            acumulado = ClasificadorLineasCompra.AcumularLinea(
                acumulado,
                category: Texto(r, "Category"),
                customField1: Texto(r, "CustomField1"),
                customField3: Texto(r, "CustomField3"),
                customField4: Texto(r, "CustomField4"),
                amount: Decimal(r, "Amount"),
                laborCost: Decimal(r, "LaborCost"));
        }

        return ClasificadorLineasCompra.Redondear(acumulado);
    }

    private static string Texto(System.Data.Common.DbDataReader r, string columna)
    {
        var i = r.GetOrdinal(columna);
        return r.IsDBNull(i) ? string.Empty : r.GetValue(i)?.ToString() ?? string.Empty;
    }

    private static decimal Decimal(System.Data.Common.DbDataReader r, string columna)
    {
        var i = r.GetOrdinal(columna);
        return r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i));
    }
}
