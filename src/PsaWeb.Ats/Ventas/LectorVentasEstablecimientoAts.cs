using System.Data.Odbc;
using PsaWeb.Ats.Esquema;
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Ats.Ventas;

/// <summary>
/// Total de ventas por establecimiento (los 3 primeros dígitos de
/// <c>JrnlHdr.Reference</c>), directo de Sage 50. Port de la 2ª consulta de
/// <c>LoadSales</c>.
/// </summary>
/// <remarks>
/// El <c>GROUP BY LEFT(Reference,3)</c> ya deja un renglón por establecimiento
/// — el `.exe` además acumula del lado del cliente por si se repite un
/// <c>codEstab</c>, pero eso nunca puede pasar dado el <c>GROUP BY</c>; acá se
/// omite esa fusión redundante.
/// </remarks>
public static class LectorVentasEstablecimientoAts
{
    private static readonly string Sql = $"""
        SELECT LEFT(JrnlHdr.Reference, 3) AS CodEstab, SUM(JrnlRow.Amount) AS Monto
        FROM JrnlHdr, JrnlRow
        WHERE JrnlHdr.PostOrder = JrnlRow.PostOrder
          AND JrnlHdr.JrnlKey_Journal = {DiarioSage.Ventas}
          AND "MONTH"(JrnlHdr.TransactionDate) = ?
          AND "YEAR"(JrnlHdr.TransactionDate) = ?
          AND JrnlHdr.Reference LIKE '___-___-%'
          AND (JrnlHdr.JournalEx = {DiarioSage.JournalExFacturaVenta} OR JrnlHdr.JournalEx = {DiarioSage.JournalExNotaCreditoVenta})
          AND JrnlRow.RowNumber > 0
          AND JrnlRow.RowType = 0
          AND JrnlRow.Amount <> 0
          AND JrnlHdr.PurchOrder <> '4R'
        GROUP BY LEFT(JrnlHdr.Reference, 3)
        """;

    public static async Task<IReadOnlyList<ventaEstType>> LeerAsync(
        OdbcConnection connection, int periodo, int mes, CancellationToken cancellationToken = default)
    {
        await using var cmd = new OdbcCommand(Sql, connection);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = mes });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = periodo });

        var resultado = new List<ventaEstType>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            var codEstab = r.GetValue(r.GetOrdinal("CodEstab")).ToString() ?? string.Empty;
            var monto = Convert.ToDecimal(r.GetValue(r.GetOrdinal("Monto")));

            resultado.Add(new ventaEstType
            {
                codEstab = codEstab,
                ventasEstab = Math.Round(Math.Abs(monto), 2),
                ivaComp = 0,
                ivaCompSpecified = true,
            });
        }

        return resultado;
    }
}
