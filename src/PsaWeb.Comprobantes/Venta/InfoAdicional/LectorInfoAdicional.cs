using System.Data.Odbc;
using System.Text.RegularExpressions;
using PsaWeb.Datil.Model;

namespace PsaWeb.Comprobantes.Venta.InfoAdicional;

/// <summary>
/// Arma la lista ordenada de información adicional de una factura a partir de su
/// configuración (<see cref="ConfigInfoAdicional"/>) y de Sage 50.
/// Port de <c>LoadSaleInvoice.LoadAditionalInfoOnInvoice</c>.
/// </summary>
public static partial class LectorInfoAdicional
{
    // La columna/expresión viene de una tabla de configuración de PSA (no del
    // usuario), pero igual se valida como identificador SQL simple antes de
    // interpolarla, porque no se puede parametrizar un nombre de columna.
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.]*$")]
    private static partial Regex IdentificadorSql();

    private const string JrnlHdr = "JrnlHdr";
    private const string JrnlRow = "JrnlRow";
    private const string Customers = "Customers";

    public static async Task<IReadOnlyList<InfoAdicionalItem>> ArmarAsync(
        OdbcConnection conexion,
        string postOrder,
        IReadOnlyList<ConfigInfoAdicional> config,
        CancellationToken cancellationToken = default)
    {
        var acumulado = new List<(string Nombre, string Valor, int Orden)>();

        foreach (var c in config.OrderBy(x => x.OrderNum))
        {
            if (string.IsNullOrEmpty(c.SourceTable))
            {
                if (!string.IsNullOrEmpty(c.ValueAllTime))
                {
                    acumulado.Add((c.Nombre, c.ValueAllTime, c.OrderNum));
                }
                continue;
            }

            if (string.IsNullOrEmpty(c.SourceValue) || !IdentificadorSql().IsMatch(c.SourceValue))
            {
                continue; // configuración inválida: se ignora, igual que un valor vacío
            }

            switch (c.SourceTable)
            {
                case JrnlHdr:
                    await LeerJrnlHdrAsync(conexion, postOrder, c, acumulado, cancellationToken);
                    break;
                case JrnlRow:
                    await LeerJrnlRowAsync(conexion, postOrder, c, acumulado, cancellationToken);
                    break;
                case Customers:
                    await LeerCustomersAsync(conexion, postOrder, c, acumulado, cancellationToken);
                    break;
            }
        }

        return acumulado
            .OrderBy(x => x.Orden)
            .Select(x => new InfoAdicionalItem { Nombre = x.Nombre, Valor = x.Valor })
            .ToList();
    }

    private static async Task LeerJrnlHdrAsync(
        OdbcConnection conexion, string postOrder, ConfigInfoAdicional c,
        List<(string, string, int)> acumulado, CancellationToken ct)
    {
        var sql = $"SELECT {c.SourceValue} AS CampoToValue FROM JrnlHdr WHERE (PostOrder = ?)";
        await using var cmd = new OdbcCommand(sql, conexion);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = postOrder });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (await r.ReadAsync(ct) && !await r.IsDBNullAsync(0, ct))
        {
            var valor = r.GetValue(0)?.ToString() ?? string.Empty;
            if (valor.Length > 0)
            {
                acumulado.Add((c.Nombre, valor, c.OrderNum));
            }
        }
    }

    private static async Task LeerJrnlRowAsync(
        OdbcConnection conexion, string postOrder, ConfigInfoAdicional c,
        List<(string, string, int)> acumulado, CancellationToken ct)
    {
        var sql =
            $"SELECT {c.SourceValue} AS CampoToValue, RowNumber FROM JrnlRow " +
            "WHERE (RowType = 0) AND (RowNumber > 0) AND (Amount = 0 OR Amount > 0) " +
            "AND (Quantity = 0) AND (PostOrder = ?) ORDER BY RowNumber";
        await using var cmd = new OdbcCommand(sql, conexion);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = postOrder });
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var primeraFila = 0;
        while (await r.ReadAsync(ct))
        {
            if (await r.IsDBNullAsync(0, ct))
            {
                continue;
            }

            var rowNumber = Convert.ToInt32(r.GetValue(1)?.ToString());
            var valor = r.GetValue(0)?.ToString() ?? string.Empty;
            if (primeraFila == 0)
            {
                primeraFila = rowNumber - 1;
            }
            if (valor.Length == 0)
            {
                continue;
            }

            var nombre = valor.ToUpperInvariant().Contains("COMISI")
                ? "Comisión"
                : $"{c.Nombre} {rowNumber - primeraFila}";
            acumulado.Add((nombre, valor, c.OrderNum + rowNumber));
        }
    }

    private static async Task LeerCustomersAsync(
        OdbcConnection conexion, string postOrder, ConfigInfoAdicional c,
        List<(string, string, int)> acumulado, CancellationToken ct)
    {
        var sql =
            $"SELECT {c.SourceValue} AS CampoToValue FROM JrnlHdr, Customers " +
            "WHERE Customers.CustomerRecordNumber = JrnlHdr.CustVendId AND (PostOrder = ?)";
        await using var cmd = new OdbcCommand(sql, conexion);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = postOrder });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            if (await r.IsDBNullAsync(0, ct))
            {
                continue;
            }
            var valor = r.GetValue(0)?.ToString() ?? string.Empty;
            if (valor.Length > 0)
            {
                acumulado.Add((c.Nombre, valor, c.OrderNum));
            }
        }
    }
}
