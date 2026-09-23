using System.Data.Odbc;
using PsaWeb.Comprobantes.Clientes;
using PsaWeb.Comprobantes.Compras;
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>
/// Lee las retenciones de venta recibidas del período (el Set B de la conciliación para los
/// "Comprobante de Retención" del SRI). En Sage se registran como notas de crédito de ventas
/// (<c>JournalEx = 9</c>) marcadas con <c>PurchOrder = '4R'</c> ("Customer PO"); las notas de crédito de
/// venta emitidas llevan '4' y se excluyen — misma convención que ya usa el ATS
/// (<c>LectorDetalleVentaAts.SqlRetencionRecibida</c>). Sin equivalente en el `.exe` para conciliar.
/// <para>
/// El monto es el total del comprobante (<c>MainAmount</c>), no el desglose IRF/IVA por <c>Jobs</c> del ATS:
/// alcanza para mostrarlo y no depende de cómo se codifiquen los jobs. Todavía no se compara contra el SRI
/// porque el reporte del portal no trae montos para las retenciones.
/// </para>
/// </summary>
public static class LectorRetencionesRecibidasParaConciliacion
{
    private static readonly string Sql = $"""
        SELECT JrnlHdr.CustVendId AS CustVendId, JrnlHdr.PostOrder AS PostOrder,
               JrnlHdr.Reference AS Reference, JrnlHdr.TransactionDate AS TransactionDate,
               JrnlHdr.MainAmount AS MainAmount
        FROM JrnlHdr
        WHERE JrnlHdr.JrnlKey_Journal = {DiarioSage.Ventas}
          AND JrnlHdr.JournalEx = {DiarioSage.JournalExNotaCreditoVenta}
          AND JrnlHdr.PurchOrder = '4R'
          AND NOT (JrnlHdr.Description LIKE 'ANULAD%')
          AND JrnlHdr.Reference LIKE '___-___-%'
          AND JrnlHdr.TransactionDate BETWEEN ? AND ?
        """;

    public static async Task<IReadOnlyList<CompraSage>> LeerAsync(
        OdbcConnection connection, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken = default)
    {
        var filas = new List<(long CustomerId, long PostOrder, string Reference, DateTime Fecha, decimal Monto)>();

        await using (var cmd = new OdbcCommand(Sql, connection))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = desde.ToDateTime(TimeOnly.MinValue) });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = hasta.ToDateTime(TimeOnly.MinValue) });

            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                var monto = r.GetValue(r.GetOrdinal("MainAmount"));
                filas.Add((
                    Convert.ToInt64(r.GetValue(r.GetOrdinal("CustVendId"))),
                    Convert.ToInt64(r.GetValue(r.GetOrdinal("PostOrder"))),
                    r.GetValue(r.GetOrdinal("Reference")).ToString() ?? string.Empty,
                    Convert.ToDateTime(r.GetValue(r.GetOrdinal("TransactionDate"))),
                    monto is DBNull ? 0m : Math.Abs(Convert.ToDecimal(monto))));
            }
        }

        var resultado = new List<CompraSage>(filas.Count);
        foreach (var fila in filas)
        {
            var cliente = await LectorCliente.LeerAsync(connection, fila.CustomerId.ToString(), cancellationToken);
            // Si la retención trae AUT-SRI se usa para cruzar por clave; si no, el motor cruza por RUC + serie.
            var autorizacion = await LectorAuxiliarCompras.AutorizacionPropiaAsync(connection, fila.PostOrder, cancellationToken);

            resultado.Add(new CompraSage(
                PostOrder: fila.PostOrder,
                RucProveedor: cliente?.Identificacion ?? string.Empty,
                NombreProveedor: cliente?.RazonSocial ?? string.Empty,
                Referencia: fila.Reference,
                Fecha: DateOnly.FromDateTime(fila.Fecha),
                Autorizacion: autorizacion,
                Subtotal: 0m,
                Iva: 0m,
                Total: fila.Monto,
                Tipo: TipoDocumentoRecibido.Retencion));
        }

        return resultado;
    }
}
