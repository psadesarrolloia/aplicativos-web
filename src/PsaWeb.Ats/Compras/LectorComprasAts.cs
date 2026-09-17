using System.Data.Odbc;
using PsaWeb.Ats.Esquema;
using PsaWeb.Comprobantes.Compras;
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Ats.Compras;

/// <summary>
/// Lee de Sage 50 las compras del período (facturas, notas de venta,
/// liquidaciones y notas de crédito). Port de
/// <c>ATSModel.LoadPurchases</c> (<c>ATSfromPeach</c>) — el bloque más grande
/// del ATS.
/// </summary>
public static class LectorComprasAts
{
    private static readonly string Sql = $"""
        SELECT JrnlHdr.CustVendId AS CustVendId, JrnlHdr.JournalEx AS JournalEx,
               JrnlHdr.PostOrder AS PostOrder, JrnlHdr.Reference AS Reference,
               JrnlHdr.ShipVia AS ShipVia, JrnlHdr.TransactionDate AS TransactionDate,
               JrnlHdr.ShipToAddress2 AS ShipToAddress2, JrnlHdr.ShipToCity AS ShipToCity
        FROM JrnlHdr, Vendors
        WHERE JrnlHdr.CustVendId = Vendors.VendorRecordNumber
          AND "MONTH"(JrnlHdr.TransactionDate) = ?
          AND "YEAR"(JrnlHdr.TransactionDate) = ?
          AND JrnlHdr.JrnlKey_Journal = {DiarioSage.Compras}
          AND NOT (Vendors.VendorID LIKE 'ANULAD%')
          AND JrnlHdr.Reference LIKE '___-___-%'
        """;

    public static async Task<IReadOnlyList<detalleComprasType>> LeerAsync(
        OdbcConnection connection,
        int periodo,
        int mes,
        IReadOnlyList<CodigoIdentificacionCompras> catalogoIdentificacion,
        CancellationToken cancellationToken = default)
    {
        var filas = new List<(long VendorId, int JournalEx, long PostOrder, string Reference, string ShipVia, DateTime Fecha, string ShipToAddress2, string ShipToCity)>();

        await using (var cmd = new OdbcCommand(Sql, connection))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = mes });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = periodo });

            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                filas.Add((
                    Convert.ToInt64(r.GetValue(r.GetOrdinal("CustVendId"))),
                    Convert.ToInt32(r.GetValue(r.GetOrdinal("JournalEx"))),
                    Convert.ToInt64(r.GetValue(r.GetOrdinal("PostOrder"))),
                    r.GetValue(r.GetOrdinal("Reference")).ToString() ?? string.Empty,
                    (r.GetValue(r.GetOrdinal("ShipVia"))?.ToString() ?? string.Empty),
                    Convert.ToDateTime(r.GetValue(r.GetOrdinal("TransactionDate"))),
                    r.IsDBNull(r.GetOrdinal("ShipToAddress2")) ? string.Empty : r.GetValue(r.GetOrdinal("ShipToAddress2")).ToString() ?? string.Empty,
                    r.IsDBNull(r.GetOrdinal("ShipToCity")) ? string.Empty : r.GetValue(r.GetOrdinal("ShipToCity")).ToString() ?? string.Empty));
            }
        }

        // Autoretenciones que el SRI exige a los Grandes Contribuyentes: son
        // una obligación interna de Sage 50, no una compra a un tercero — no
        // deben reflejarse en el ATS. Convención acordada con el usuario
        // 2026-09-12: marcarlas en Sage con ShipVia="AUTORETENCION".
        filas.RemoveAll(f => LectorAuxiliarCompras.EsAutoretencion(f.ShipVia));

        var resultado = new List<detalleComprasType>(filas.Count);
        foreach (var fila in filas)
        {
            var (tipoComprobante, codSustento) = await ResolverTipoYSustentoAsync(connection, fila.JournalEx, fila.PostOrder, fila.ShipVia, cancellationToken);

            // ShipToCity puede traer directamente un código de sustento ("01"/
            // "02") que pisa al calculado — solo aplica a factura/NV/liq
            // (JournalEx 11); el `.exe` lo evalúa siempre, pero para NC
            // (JournalEx 12) ShipToCity no se usa con este propósito.
            if (fila.JournalEx == DiarioSage.JournalExFacturaCompra
                && fila.ShipToCity is CodigosSustentoAts.Credito or CodigosSustentoAts.Costo
                && codSustento != fila.ShipToCity)
            {
                codSustento = fila.ShipToCity;
            }

            var proveedor = await LectorProveedorAts.LeerAsync(connection, fila.VendorId, catalogoIdentificacion, cancellationToken);

            var fechaTexto = fila.Fecha.ToString("dd/MM/yyyy");
            var establecimiento = fila.Reference[..3];
            var puntoEmision = fila.Reference.Substring(4, 3);
            var secuencial = fila.Reference[8..];
            var autorizacion = await LectorAuxiliarCompras.NumeroAutorizacionAsync(connection, fila.PostOrder, cancellationToken);
            var buckets = await LectorImponiblesCompra.LeerImponiblesAsync(connection, fila.PostOrder, cancellationToken);

            detalleAirComprasType[]? retencionesRenta = null;
            string? numeroCompletoOriginal = null;
            string? autorizacionOriginal = null;

            if (tipoComprobante is TiposComprobanteComprasAts.Factura or TiposComprobanteComprasAts.NotaVenta or TiposComprobanteComprasAts.Liquidacion)
            {
                retencionesRenta = await LectorDetalleComprasAts.LeerRetencionesRentaAsync(connection, fila.PostOrder, cancellationToken);
            }
            else if (tipoComprobante == TiposComprobanteComprasAts.NotaCredito)
            {
                numeroCompletoOriginal = await LectorAuxiliarCompras.NumeroCompletoDeCompraOriginalAsync(connection, fila.PostOrder, cancellationToken);
                if (!string.IsNullOrEmpty(numeroCompletoOriginal))
                {
                    var postOrderOriginal = await LectorAuxiliarCompras.PostOrderCompraOriginalAsync(connection, fila.PostOrder, cancellationToken);
                    autorizacionOriginal = postOrderOriginal is not null
                        ? await LectorAuxiliarCompras.NumeroAutorizacionAsync(connection, postOrderOriginal.Value, cancellationToken)
                        : string.Empty;
                }
            }

            var crudo = new CompraCruda(
                TipoComprobante: tipoComprobante,
                CodSustento: codSustento,
                Establecimiento: establecimiento,
                PuntoEmision: puntoEmision,
                Secuencial: secuencial,
                FechaRegistro: fechaTexto,
                FechaEmision: fechaTexto,
                Autorizacion: autorizacion,
                Proveedor: proveedor,
                Buckets: buckets,
                ShipToAddress2: fila.ShipToAddress2,
                ShipToCity: fila.ShipToCity,
                RetencionesRenta: retencionesRenta,
                NumeroCompletoCompraOriginal: numeroCompletoOriginal,
                AutorizacionCompraOriginal: autorizacionOriginal);

            resultado.Add(ArmadorComprasAts.Armar(crudo));
        }

        return resultado;
    }

    /// <summary>
    /// Port de la clasificación inicial de <c>LoadPurchases</c>: JournalEx 12
    /// = nota de crédito (el sustento se hereda de la compra original);
    /// JournalEx 11 = factura/nota de venta/liquidación según <c>ShipVia</c>.
    /// </summary>
    private static async Task<(string TipoComprobante, string CodSustento)> ResolverTipoYSustentoAsync(
        OdbcConnection connection, int journalEx, long postOrder, string shipVia, CancellationToken cancellationToken)
    {
        if (journalEx == DiarioSage.JournalExNotaCreditoCompra)
        {
            var postOrderOriginal = await LectorAuxiliarCompras.PostOrderCompraOriginalAsync(connection, postOrder, cancellationToken);
            var codSustento = string.Empty;
            if (postOrderOriginal is not null)
            {
                codSustento = await LectorAuxiliarCompras.CodigoSustentoCostoAsync(connection, postOrderOriginal.Value, cancellationToken);
            }

            return (TiposComprobanteComprasAts.NotaCredito, codSustento);
        }

        if (journalEx == DiarioSage.JournalExFacturaCompra)
        {
            var viaTrim = shipVia.Trim();
            if (shipVia == "1" || viaTrim == "FACTURA")
            {
                return (TiposComprobanteComprasAts.Factura, await LectorAuxiliarCompras.CodigoSustentoCostoAsync(connection, postOrder, cancellationToken));
            }

            if (shipVia == "2" || viaTrim == "NOTA DE VENTA")
            {
                return (TiposComprobanteComprasAts.NotaVenta, await LectorAuxiliarCompras.CodigoSustentoCostoAsync(connection, postOrder, cancellationToken));
            }

            if (shipVia == "3" || viaTrim.Contains("LIQUIDACION"))
            {
                return (TiposComprobanteComprasAts.Liquidacion, await LectorAuxiliarCompras.CodigoSustentoCostoAsync(connection, postOrder, cancellationToken));
            }

            return (TiposComprobanteComprasAts.Desconocido, TiposComprobanteComprasAts.Desconocido);
        }

        // Ni factura/NV/liq (11) ni NC (12): el `.exe` deja tipoComprobante y
        // codSustento sin asignar (quedan null en el esquema).
        return (string.Empty, string.Empty);
    }
}
