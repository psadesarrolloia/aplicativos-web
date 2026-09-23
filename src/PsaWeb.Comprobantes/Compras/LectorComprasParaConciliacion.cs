using System.Data.Odbc;
using PsaWeb.Comprobantes.Proveedores;
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Comprobantes.Compras;

/// <summary>
/// Una compra de Sage 50, para el motor de conciliación SRI (Set B, §13.1 del
/// plan). Da un total simple por comprobante (vía <see cref="BucketsCompra"/>)
/// en vez del desglose por tarifa de IVA que necesita el ATS — no comparte
/// tipo con <c>PsaWeb.Ats.Esquema.detalleComprasType</c> a propósito.
/// </summary>
/// <param name="RucProveedor">RUC de la contraparte: el proveedor (facturas/NC) o el cliente que retuvo (retenciones).</param>
/// <param name="Tipo">Factura o nota de crédito de compra; <see cref="TipoDocumentoRecibido.Retencion"/> para las retenciones de venta recibidas.</param>
/// <param name="DocumentoModificado">Solo notas de crédito: la factura a la que se aplica (tal como está en Sage).</param>
public sealed record CompraSage(
    long PostOrder,
    string RucProveedor,
    string NombreProveedor,
    string Referencia,
    DateOnly Fecha,
    string Autorizacion,
    decimal Subtotal,
    decimal Iva,
    decimal Total,
    TipoDocumentoRecibido Tipo = TipoDocumentoRecibido.Factura,
    string? DocumentoModificado = null);

/// <summary>
/// Lee las compras del período (Set B de la conciliación SRI) — mismo filtro
/// base que <c>PsaWeb.Ats.Compras.LectorComprasAts</c> (diario de compras,
/// referencia con formato de comprobante, sin autoretenciones) pero sin la
/// clasificación por tipo/sustento tributario que solo necesita el ATS. Los
/// montos vienen de <see cref="LectorImponiblesCompra"/> — ya validado contra
/// datos reales de Sage por el ATS, no una consulta <c>SUM</c> nueva (§13.1:
/// sumar todas las líneas de un asiento de partida doble da 0, no el total).
/// </summary>
public static class LectorComprasParaConciliacion
{
    private static readonly string Sql = $"""
        SELECT JrnlHdr.CustVendId AS CustVendId, JrnlHdr.PostOrder AS PostOrder,
               JrnlHdr.Reference AS Reference, JrnlHdr.ShipVia AS ShipVia,
               JrnlHdr.TransactionDate AS TransactionDate, JrnlHdr.JournalEx AS JournalEx
        FROM JrnlHdr, Vendors
        WHERE JrnlHdr.CustVendId = Vendors.VendorRecordNumber
          AND JrnlHdr.TransactionDate BETWEEN ? AND ?
          AND JrnlHdr.JrnlKey_Journal = {DiarioSage.Compras}
          AND NOT (Vendors.VendorID LIKE 'ANULAD%')
          AND JrnlHdr.Reference LIKE '___-___-%'
        """;

    public static async Task<IReadOnlyList<CompraSage>> LeerAsync(
        OdbcConnection connection, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken = default)
    {
        var filas = new List<(long VendorId, long PostOrder, string Reference, string ShipVia, DateTime Fecha, bool EsNotaCredito)>();

        await using (var cmd = new OdbcCommand(Sql, connection))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = desde.ToDateTime(TimeOnly.MinValue) });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = hasta.ToDateTime(TimeOnly.MinValue) });

            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                filas.Add((
                    Convert.ToInt64(r.GetValue(r.GetOrdinal("CustVendId"))),
                    Convert.ToInt64(r.GetValue(r.GetOrdinal("PostOrder"))),
                    r.GetValue(r.GetOrdinal("Reference")).ToString() ?? string.Empty,
                    r.GetValue(r.GetOrdinal("ShipVia"))?.ToString() ?? string.Empty,
                    Convert.ToDateTime(r.GetValue(r.GetOrdinal("TransactionDate"))),
                    EsNotaCreditoCompra(r.GetValue(r.GetOrdinal("JournalEx")))));
            }
        }

        // Autoretenciones (§5.5.1 de ESTADO-MIGRACION-WEB.md): obligación interna
        // de Sage, no una compra a un tercero — nunca van a aparecer en el
        // reporte del SRI, así que se excluyen acá igual que en el ATS.
        filas.RemoveAll(f => LectorAuxiliarCompras.EsAutoretencion(f.ShipVia));

        var resultado = new List<CompraSage>(filas.Count);
        foreach (var fila in filas)
        {
            var proveedor = await LectorProveedor.LeerAsync(connection, fila.VendorId.ToString(), cancellationToken);
            // En una nota de crédito el AUT-SRI que cuenta es el suyo: el vínculo a otra transacción que
            // sigue NumeroAutorizacionAsync (pensado para la orden de compra) apuntaría a la factura original.
            var autorizacion = fila.EsNotaCredito
                ? await LectorAuxiliarCompras.AutorizacionPropiaAsync(connection, fila.PostOrder, cancellationToken)
                : await LectorAuxiliarCompras.NumeroAutorizacionAsync(connection, fila.PostOrder, cancellationToken);
            var buckets = await LectorImponiblesCompra.LeerImponiblesAsync(connection, fila.PostOrder, cancellationToken);
            var documentoModificado = fila.EsNotaCredito
                ? NullSiVacio(await LectorAuxiliarCompras.NumeroCompletoDeCompraOriginalAsync(connection, fila.PostOrder, cancellationToken))
                : null;

            resultado.Add(new CompraSage(
                PostOrder: fila.PostOrder,
                RucProveedor: proveedor?.Identificacion ?? string.Empty,
                NombreProveedor: proveedor?.RazonSocial ?? string.Empty,
                Referencia: fila.Reference,
                Fecha: DateOnly.FromDateTime(fila.Fecha),
                Autorizacion: autorizacion,
                Subtotal: buckets.Subtotal,
                Iva: buckets.MontoIva,
                Total: buckets.Total,
                Tipo: fila.EsNotaCredito ? TipoDocumentoRecibido.NotaCredito : TipoDocumentoRecibido.Factura,
                DocumentoModificado: documentoModificado));
        }

        return resultado;
    }

    // JournalEx de las notas de crédito de compra (Vendor Credit Memos): el mismo diario de compras
    // que las facturas, distinguidas por este valor (constante ya usada por el ATS).
    private static bool EsNotaCreditoCompra(object? journalEx) =>
        journalEx is not null and not DBNull && Convert.ToInt32(journalEx) == DiarioSage.JournalExNotaCreditoCompra;

    private static string? NullSiVacio(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
