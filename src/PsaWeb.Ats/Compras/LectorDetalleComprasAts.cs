using System.Data.Odbc;
using PsaWeb.Ats.Esquema;
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Ats.Compras;

/// <summary>
/// Lee de Sage 50 las bases imponibles (<c>LoadImponibles</c>) y las
/// retenciones de renta (<c>LoadRetencionesRF</c>) de una compra por
/// <c>PostOrder</c>. Port de <c>ATSfromPeach.ATSModel.LoadPurchases</c>.
/// </summary>
public static class LectorDetalleComprasAts
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

    // Retenciones de renta con porcentaje > 0 (categoría R-...RF, excepto 332).
    private static readonly string SqlRetencionesConPorcentaje = $"""
        SELECT LineItem.Category, LineItem.CustomField1, JrnlRow.Amount, LineItem.LaborCost, JrnlRow.Quantity
        FROM JrnlRow, LineItem
        WHERE JrnlRow.ItemRecordNumber = LineItem.ItemRecordNumber
          AND JrnlRow.Journal = {DiarioSage.Compras}
          AND JrnlRow.RowType = 0
          AND JrnlRow.RowNumber > 0
          AND LineItem.Category LIKE 'R-%RF'
          AND JrnlRow.PostOrder = ?
        ORDER BY JrnlRow.RowNumber
        """;

    // Retenciones con porcentaje 0 (332/332G/...), agrupadas por CustomField5.
    private static readonly string SqlRetencionesSinPorcentaje = $"""
        SELECT SUM(JrnlRow.Amount) AS Amount, LineItem.CustomField5
        FROM JrnlRow, LineItem
        WHERE JrnlRow.ItemRecordNumber = LineItem.ItemRecordNumber
          AND JrnlRow.Journal = {DiarioSage.Compras}
          AND JrnlRow.RowType = 0
          AND JrnlRow.RowNumber > 0
          AND LineItem.CustomField1 LIKE '%COMPRA%'
          AND JrnlRow.PostOrder = ?
        GROUP BY LineItem.CustomField5
        """;

    public static async Task<BucketsComprasAts> LeerImponiblesAsync(
        OdbcConnection connection, long postOrder, CancellationToken cancellationToken = default)
    {
        await using var cmd = new OdbcCommand(SqlImponibles, connection);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = postOrder });

        var acumulado = BucketsComprasAts.Cero;
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            acumulado = ClasificadorLineasComprasAts.AcumularLinea(
                acumulado,
                category: Texto(r, "Category"),
                customField1: Texto(r, "CustomField1"),
                customField3: Texto(r, "CustomField3"),
                customField4: Texto(r, "CustomField4"),
                amount: Decimal(r, "Amount"),
                laborCost: Decimal(r, "LaborCost"));
        }

        return ClasificadorLineasComprasAts.Redondear(acumulado);
    }

    public static async Task<detalleAirComprasType[]?> LeerRetencionesRentaAsync(
        OdbcConnection connection, long postOrder, CancellationToken cancellationToken = default)
    {
        var detalles = new List<detalleAirComprasType>();

        await using (var cmd = new OdbcCommand(SqlRetencionesConPorcentaje, connection))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = postOrder });
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                var codigo = Texto(r, "CustomField1");

                // Las líneas 332 se reportan aparte (sin porcentaje, ver abajo)
                // — el `.exe` las descarta acá para no duplicarlas.
                if (codigo.Contains("332"))
                {
                    continue;
                }

                var cantidad = Decimal(r, "Quantity");
                var laborCost = Decimal(r, "LaborCost");
                var amount = Decimal(r, "Amount");

                detalles.Add(new detalleAirComprasType
                {
                    codRetAir = codigo,
                    baseImpAir = Math.Round(Math.Abs(Math.Round(cantidad, 4)), 2),
                    porcentajeAir = Math.Round(Math.Abs(Math.Round(laborCost, 4)) * 100, 2),
                    valRetAir = Math.Round(Math.Abs(amount), 2),
                });
            }
        }

        await using (var cmd = new OdbcCommand(SqlRetencionesSinPorcentaje, connection))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = postOrder });
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                var codigo = TextoONull(r, "CustomField5") ?? string.Empty;
                if (codigo.Length == 0)
                {
                    continue;
                }

                var amount = Decimal(r, "Amount");
                detalles.Add(new detalleAirComprasType
                {
                    codRetAir = codigo,
                    baseImpAir = Math.Round(Math.Abs(amount), 2),
                    porcentajeAir = 0,
                    valRetAir = 0,
                });
            }
        }

        return detalles.Count > 0 ? detalles.ToArray() : null;
    }

    private static string Texto(System.Data.Common.DbDataReader r, string columna)
    {
        var i = r.GetOrdinal(columna);
        return r.IsDBNull(i) ? string.Empty : r.GetValue(i)?.ToString() ?? string.Empty;
    }

    private static string? TextoONull(System.Data.Common.DbDataReader r, string columna)
    {
        var i = r.GetOrdinal(columna);
        return r.IsDBNull(i) ? null : r.GetValue(i)?.ToString();
    }

    private static decimal Decimal(System.Data.Common.DbDataReader r, string columna)
    {
        var i = r.GetOrdinal(columna);
        return r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i));
    }
}
