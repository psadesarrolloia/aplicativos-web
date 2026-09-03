using System.Data.Common;
using System.Data.Odbc;
using PsaWeb.Comprobantes.Proveedores;
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Comprobantes.Retenciones;

/// <summary>Compra + proveedor leídos de Sage 50 para una retención pendiente.</summary>
public sealed record CompraRetencionLeida(DatosCompraRetencion Compra, ProveedorSri Proveedor);

/// <summary>
/// Lee de Sage 50 (ODBC) todo lo necesario para armar una retención a partir del
/// <c>PostOrder</c> de la factura de compra. Port de la parte ODBC de
/// <c>LoadPurchaseTwh.ForceLoadFromPeach</c>.
/// </summary>
public static class LectorCompraRetencion
{
    // Cabecera de la compra: ShipToAddress2 = número de retención, ShipVia = tipo de sustento.
    private static readonly string SqlCabecera = $"""
        SELECT JrnlHdr.ShipToAddress2 AS NumeroRetencion,
               JrnlHdr.CustVendId     AS VendorRecordNumber,
               JrnlHdr.PostOrder      AS PostOrder,
               JrnlHdr.Reference      AS NumeroFactura,
               JrnlHdr.ShipVia        AS ShipVia,
               JrnlHdr.TransactionDate AS FechaCompra,
               JrnlHdr.INV_POSOOrderNumber AS OrdenCompraRef
        FROM JrnlHdr
        WHERE JrnlHdr.JrnlKey_Journal = {DiarioSage.Compras}
          AND NOT (JrnlHdr.Description LIKE 'ANULAD%')
          AND JrnlHdr.PostOrder = ?
        """;

    // GoodThruDate de la orden de compra (fecha de emisión de la retención).
    private static readonly string SqlGoodThru = $"""
        SELECT GoodThruDate
        FROM JrnlHdr
        WHERE Reference = ? AND JrnlKey_Journal = {DiarioSage.OrdenCompra}
        """;

    // Líneas de retención: categorías R-...RF (renta) y R-IVA.
    private static readonly string SqlLineas = $"""
        SELECT LineItem.Category      AS Category,
               LineItem.CustomField1  AS CustomField1,
               LineItem.CustomField2  AS CustomField2,
               JrnlRow.Amount         AS Amount,
               JrnlRow.Quantity       AS Quantity,
               LineItem.ItemID        AS ItemId
        FROM JrnlRow, LineItem
        WHERE JrnlRow.ItemRecordNumber = LineItem.ItemRecordNumber
          AND JrnlRow.Journal = {DiarioSage.Compras}
          AND JrnlRow.RowType = 0
          AND JrnlRow.RowNumber > 0
          AND (LineItem.Category LIKE 'R-%RF' OR LineItem.Category LIKE 'R-IVA')
          AND JrnlRow.PostOrder = ?
        ORDER BY JrnlRow.RowNumber
        """;

    public static async Task<CompraRetencionLeida?> LeerAsync(
        OdbcConnection connection, string piPostOrder, CancellationToken cancellationToken = default)
    {
        var postOrder = long.Parse(piPostOrder);

        // --- Cabecera ---
        string numeroRetencion, shipVia, numeroFacturaCrudo, ordenCompraRef, vendorRecordNumber;
        DateTime fechaCompra;

        await using (var cmd = new OdbcCommand(SqlCabecera, connection))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = postOrder });
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await r.ReadAsync(cancellationToken))
            {
                return null;
            }

            numeroRetencion = Str(r, "NumeroRetencion");
            vendorRecordNumber = Str(r, "VendorRecordNumber");
            shipVia = Str(r, "ShipVia");
            numeroFacturaCrudo = Str(r, "NumeroFactura");
            ordenCompraRef = Str(r, "OrdenCompraRef");
            fechaCompra = Convert.ToDateTime(r.GetValue(r.GetOrdinal("FechaCompra")));
        }

        var numeroFactura = NumeroDocumentoSri.NormalizarNumeroFactura(numeroFacturaCrudo);

        // --- GoodThruDate ---
        DateTime? goodThru = null;
        await using (var cmd = new OdbcCommand(SqlGoodThru, connection))
        {
            cmd.Parameters.Add(new OdbcParameter { Value = ordenCompraRef });
            var val = await cmd.ExecuteScalarAsync(cancellationToken);
            if (val is not null && val != DBNull.Value)
            {
                goodThru = Convert.ToDateTime(val);
            }
        }

        // --- Líneas ---
        var lineas = new List<LineaRetencionCruda>();
        await using (var cmd = new OdbcCommand(SqlLineas, connection))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = postOrder });
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                lineas.Add(new LineaRetencionCruda(
                    Category: Str(r, "Category"),
                    CustomField1: Str(r, "CustomField1"),
                    CustomField2: Str(r, "CustomField2"),
                    Amount: ToDecimal(r, "Amount"),
                    Quantity: ToDecimal(r, "Quantity"),
                    ItemId: Str(r, "ItemId")));
            }
        }

        var proveedor = await LectorProveedor.LeerAsync(connection, vendorRecordNumber, cancellationToken)
                        ?? new ProveedorSri { Errores = new[] { $"No se encontró el proveedor {vendorRecordNumber} en Sage 50." } };

        var compra = new DatosCompraRetencion
        {
            NumeroRetencion = numeroRetencion,
            ShipVia = shipVia,
            NumeroFactura = numeroFactura,
            FechaCompra = fechaCompra,
            GoodThruDate = goodThru,
            PostOrderPeach = piPostOrder,
            Lineas = lineas,
        };

        return new CompraRetencionLeida(compra, proveedor);
    }

    private static string Str(DbDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? string.Empty : r.GetValue(i)?.ToString()?.Trim() ?? string.Empty;
    }

    private static decimal ToDecimal(DbDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i));
    }
}
