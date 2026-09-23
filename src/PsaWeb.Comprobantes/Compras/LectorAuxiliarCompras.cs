using System.Data.Odbc;
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Comprobantes.Compras;

/// <summary>
/// Consultas auxiliares chicas sobre compras de Sage 50 (port de piezas de
/// <c>LoadPurchases</c>, <c>ATSfromPeach</c>): ubicar el <c>PostOrder</c> de la
/// compra original de una NC, el código de sustento tributario, y el número de
/// autorización del SRI. Compartidas entre el ATS y Conciliación SRI (§13.1
/// del plan) — no tienen ninguna dependencia del esquema del ATS.
/// </summary>
public static class LectorAuxiliarCompras
{
    /// <summary>Port de <c>PostOrderPurchase(VendorID, PurchaseNumber)</c>.</summary>
    public static async Task<long?> PostOrderPorReferenciaAsync(
        OdbcConnection connection, long vendorId, string referencia, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT JrnlHdr.PostOrder FROM JrnlHdr WHERE JrnlHdr.Reference = ? AND JrnlHdr.CustVendId = ?";
        await using var cmd = new OdbcCommand(sql, connection);
        cmd.Parameters.Add(new OdbcParameter { Value = referencia });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = vendorId });

        long? resultado = null;
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken)) // último gana, igual que el `.exe`.
        {
            resultado = Convert.ToInt64(r.GetValue(r.GetOrdinal("PostOrder")));
        }

        return resultado;
    }

    /// <summary>
    /// Port del overload <c>PostOrderPurchase(PostOrderNC)</c>: ubica el
    /// <c>PostOrder</c> de la compra original a partir del <c>PostOrder</c> de
    /// una NC (vía <c>CustVendId</c> + <c>INV_POSOOrderNumber</c>).
    /// </summary>
    public static async Task<long?> PostOrderCompraOriginalAsync(
        OdbcConnection connection, long postOrderNc, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT CustVendId, JrnlHdr.INV_POSOOrderNumber AS Referencia FROM JrnlHdr WHERE JrnlHdr.PostOrder = ?";
        await using var cmd = new OdbcCommand(sql, connection);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = postOrderNc });

        long? vendorId = null;
        string? referencia = null;
        await using (var r = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await r.ReadAsync(cancellationToken))
            {
                vendorId = Convert.ToInt64(r.GetValue(r.GetOrdinal("CustVendId")));
                referencia = r.GetValue(r.GetOrdinal("Referencia"))?.ToString();
            }
        }

        if (vendorId is null || string.IsNullOrEmpty(referencia))
        {
            return null;
        }

        return await PostOrderPorReferenciaAsync(connection, vendorId.Value, referencia, cancellationToken);
    }

    /// <summary>Port de <c>PurchaseNumberComplete</c>.</summary>
    public static async Task<string> NumeroCompletoDeCompraOriginalAsync(
        OdbcConnection connection, long postOrderNc, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT JrnlHdr.INV_POSOOrderNumber AS Referencia FROM JrnlHdr WHERE JrnlHdr.PostOrder = ?";
        await using var cmd = new OdbcCommand(sql, connection);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = postOrderNc });

        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var resultado = string.Empty;
        while (await r.ReadAsync(cancellationToken))
        {
            resultado = r.GetValue(r.GetOrdinal("Referencia"))?.ToString() ?? string.Empty;
        }

        return resultado;
    }

    /// <summary>
    /// Port de <c>PostOrderOC</c>: la orden de compra vinculada a este
    /// comprobante, si tiene una (<c>JrnlRow.LinkToAnotherTrx</c>).
    /// </summary>
    public static async Task<long?> PostOrderOrdenDeCompraVinculadaAsync(
        OdbcConnection connection, long postOrder, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT JrnlRow.LinkToAnotherTrx AS Vinculo FROM JrnlRow WHERE JrnlRow.PostOrder = ? AND LinkToAnotherTrx > 0";
        await using var cmd = new OdbcCommand(sql, connection);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = postOrder });

        long? resultado = null;
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            resultado = Convert.ToInt64(r.GetValue(r.GetOrdinal("Vinculo")));
        }

        return resultado;
    }

    /// <summary>
    /// Port de <c>CodSustentoCosto</c>. El `.exe` hace un <c>JOIN</c> sin
    /// condición contra <c>Vendors</c> (producto cartesiano inofensivo porque
    /// <c>ShipVia</c> no depende del proveedor) — acá se omite esa tabla, el
    /// resultado es idéntico y evita el cruce innecesario.
    /// </summary>
    public static async Task<string> CodigoSustentoCostoAsync(
        OdbcConnection connection, long postOrder, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT JrnlHdr.ShipVia AS ShipVia FROM JrnlHdr WHERE JrnlHdr.PostOrder = ?";
        await using var cmd = new OdbcCommand(sql, connection);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = postOrder });

        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var codigo = "00";
        while (await r.ReadAsync(cancellationToken))
        {
            var shipVia = (r.GetValue(r.GetOrdinal("ShipVia"))?.ToString() ?? string.Empty).Trim();
            codigo = shipVia switch
            {
                "1" or "FACTURA" => CodigosSustentoAts.Credito,
                "2" or "NOTA DE VENTA" => CodigosSustentoAts.Costo,
                "3" => CodigosSustentoAts.Credito,
                _ when shipVia.Contains("LIQUIDACION") => CodigosSustentoAts.Credito,
                _ => "00",
            };
        }

        return codigo;
    }

    /// <summary>
    /// Port de <c>SriAuthorization</c>: prioridad = AUT-SRI de la orden de
    /// compra vinculada (si tiene) &gt; AUT-SRI del propio comprobante &gt;
    /// <c>ShipToAddress1</c> como respaldo.
    /// </summary>
    public static async Task<string> NumeroAutorizacionAsync(
        OdbcConnection connection, long postOrder, CancellationToken cancellationToken = default)
    {
        var respaldo = await ShipToAddress1Async(connection, postOrder, cancellationToken);
        var autorizacion = await AutSriDeAsync(connection, postOrder, cancellationToken);

        var postOrderOc = await PostOrderOrdenDeCompraVinculadaAsync(connection, postOrder, cancellationToken);
        if (postOrderOc is not null)
        {
            // Se pisa con lo que traiga la OC aunque no encuentre nada (mismo
            // comportamiento del `.exe`: si la OC no tiene AUT-SRI, autorizacion
            // no cambia, pero el "toreturn = autorizacion" de abajo sigue
            // corriendo igual).
            autorizacion = await AutSriDeAsync(connection, postOrderOc.Value, cancellationToken);
        }

        return string.IsNullOrEmpty(autorizacion) ? respaldo : autorizacion;
    }

    /// <summary>
    /// El AUT-SRI del propio comprobante, sin el salto a la orden de compra vinculada de
    /// <see cref="NumeroAutorizacionAsync"/>: en una nota de crédito de compra o en una retención recibida
    /// ese vínculo (si existe) apunta a la factura original, cuya clave NO es la del comprobante.
    /// Sin equivalente en el `.exe` (el ATS no concilia contra el SRI).
    /// </summary>
    public static async Task<string> AutorizacionPropiaAsync(
        OdbcConnection connection, long postOrder, CancellationToken cancellationToken = default)
    {
        var autorizacion = await AutSriDeAsync(connection, postOrder, cancellationToken);
        return string.IsNullOrEmpty(autorizacion)
            ? await ShipToAddress1Async(connection, postOrder, cancellationToken)
            : autorizacion;
    }

    /// <summary>
    /// true si la compra es una autoretención interna (convención 2026-09-12:
    /// <c>ShipVia</c> == "AUTORETENCION", sin distinguir mayúsculas/espacios)
    /// que el SRI exige a los Grandes Contribuyentes registrar en Sage 50 pero
    /// que no es una compra real a un tercero — no debe entrar al ATS ni a
    /// Conciliación SRI (nunca va a aparecer en el reporte del SRI).
    /// </summary>
    public static bool EsAutoretencion(string? shipVia) =>
        string.Equals(shipVia?.Trim(), "AUTORETENCION", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ShipToAddress1Async(OdbcConnection connection, long postOrder, CancellationToken ct)
    {
        const string sql = "SELECT JrnlHdr.ShipToAddress1 AS Autorizacion FROM JrnlHdr WHERE JrnlHdr.JournalEx = ? AND JrnlHdr.PostOrder = ?";
        await using var cmd = new OdbcCommand(sql, connection);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = DiarioSage.JournalExFacturaCompra });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = postOrder });

        await using var r = await cmd.ExecuteReaderAsync(ct);
        var resultado = string.Empty;
        while (await r.ReadAsync(ct))
        {
            resultado = r.GetValue(r.GetOrdinal("Autorizacion"))?.ToString() ?? string.Empty;
        }

        return resultado;
    }

    private static async Task<string> AutSriDeAsync(OdbcConnection connection, long postOrder, CancellationToken ct)
    {
        const string sql = """
            SELECT JrnlRow.RowDescription AS Autorizacion
            FROM JrnlRow, LineItem
            WHERE JrnlRow.ItemRecordNumber = LineItem.ItemRecordNumber
              AND LineItem.ItemID = 'AUT-SRI'
              AND JrnlRow.PostOrder = ?
            """;
        await using var cmd = new OdbcCommand(sql, connection);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = postOrder });

        await using var r = await cmd.ExecuteReaderAsync(ct);
        var resultado = string.Empty;
        while (await r.ReadAsync(ct))
        {
            resultado = r.GetValue(r.GetOrdinal("Autorizacion"))?.ToString() ?? string.Empty;
        }

        return resultado;
    }
}

/// <summary>Códigos de sustento tributario (Tabla 5 del ATS) — se quedan acá, no se usan fuera de compras.</summary>
public static class CodigosSustentoAts
{
    public const string Credito = "01";
    public const string Costo = "02";
}
