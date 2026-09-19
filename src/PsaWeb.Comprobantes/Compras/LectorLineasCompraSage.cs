using System.Data.Common;
using System.Data.Odbc;

namespace PsaWeb.Comprobantes.Compras;

/// <summary>
/// Una línea de una compra de Sage 50 (el desglose que va al subtotal, tal
/// como se ve en la grilla de "Purchases/Receive Inventory") — para mostrar
/// en el detalle de la conciliación SRI cuando el revisor quiere entender de
/// dónde sale una diferencia. No tiene equivalente en el <c>.exe</c> (nunca
/// necesitó desglosar líneas, solo sumar buckets — ver <see cref="LectorImponiblesCompra"/>).
/// Solo Item/Descripción/Monto (§feedback post-deploy): la cuenta contable
/// (<c>GLAccountID</c>) no existe en el ODBC de Sage — probado contra datos
/// reales y confirmado "Invalid column name" — así que no se pide.
/// </summary>
public sealed record LineaCompraSage(string? ItemId, string Descripcion, decimal Monto);

/// <summary>
/// Lee las líneas (<c>JrnlRow</c>+<c>LineItem</c>) de una compra por
/// <c>PostOrder</c> — mismo filtro base que <see cref="LectorImponiblesCompra"/>
/// (diario de compras, RowType=0, RowNumber&gt;0) pero sin clasificar en
/// buckets, para mostrar el detalle tal cual.
/// </summary>
public static class LectorLineasCompraSage
{
    // JrnlRow.Journal = 4 (DiarioSage.Compras) — literal en vez de la constante
    // porque la sintaxis "{ oj ... }" del outer join ODBC ya usa llaves, y no
    // se puede mezclar con un raw string interpolado (mismo motivo por el que
    // LectorFacturaVenta.SqlCabecera hardcodea JrnlKey_Journal = 3).
    private static readonly string Sql = """
        SELECT JrnlRow.RowDescription, JrnlRow.Amount, LineItem.ItemID
        FROM { oj JrnlRow LEFT OUTER JOIN LineItem ON JrnlRow.ItemRecordNumber = LineItem.ItemRecordNumber }
        WHERE JrnlRow.Journal = 4
          AND JrnlRow.RowType = 0
          AND JrnlRow.RowNumber > 0
          AND JrnlRow.PostOrder = ?
        ORDER BY JrnlRow.RowNumber
        """;

    public static async Task<IReadOnlyList<LineaCompraSage>> LeerAsync(
        OdbcConnection connection, long postOrder, CancellationToken cancellationToken = default)
    {
        await using var cmd = new OdbcCommand(Sql, connection);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = postOrder });

        var lineas = new List<LineaCompraSage>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            lineas.Add(new LineaCompraSage(
                ItemId: TextoNull(r, "ItemID"),
                Descripcion: Texto(r, "RowDescription"),
                Monto: Decimal(r, "Amount")));
        }

        return lineas;
    }

    private static string Texto(DbDataReader r, string columna)
    {
        var i = r.GetOrdinal(columna);
        return r.IsDBNull(i) ? string.Empty : r.GetValue(i)?.ToString() ?? string.Empty;
    }

    private static string? TextoNull(DbDataReader r, string columna)
    {
        var i = r.GetOrdinal(columna);
        return r.IsDBNull(i) ? null : r.GetValue(i)?.ToString();
    }

    private static decimal Decimal(DbDataReader r, string columna)
    {
        var i = r.GetOrdinal(columna);
        return r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i));
    }
}
