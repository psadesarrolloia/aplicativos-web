using System.Data.Common;
using System.Data.Odbc;
using PsaWeb.Comprobantes.Clientes;
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>
/// Lee una nota de crédito de venta de Sage 50 por su <c>PostOrder</c> y arma un
/// <see cref="NotaCreditoLeida"/>. Port de <c>LoadSaleNC.ForceLoadFromPeach</c>.
/// Resuelve además la factura modificada (por <c>INV_POSOOrderNumber</c>) usando
/// <see cref="LectorFacturaVenta"/>. La lógica pura está en <see cref="ArmarDesde"/>.
/// </summary>
public static class LectorNotaCredito
{
    private const string SqlCabecera = """
        SELECT JrnlHdr.Reference, JrnlHdr.MainAmount, JrnlHdr.CustVendId AS CustomerId,
               JrnlHdr.TransactionDate, JrnlHdr.INV_POSOOrderNumber AS InvNumber,
               JrnlHdr.ReturnAuthorization, Tax_Code.Description AS Tax
        FROM { oj JrnlHdr LEFT OUTER JOIN Tax_Code ON JrnlHdr.SalesTaxCode = Tax_Code.ID }
        WHERE (JrnlHdr.JrnlKey_Journal = 3) AND (JrnlHdr.JrnlTypeEx = 2)
          AND (NOT (JrnlHdr.Description LIKE 'ANULAD%')) AND (NOT (JrnlHdr.PurchOrder LIKE '4R'))
          AND (JrnlHdr.JournalEx = 9)
          AND (JrnlHdr.PostOrder = ?)
        """;

    private const string SqlFacturaRelacionada = """
        SELECT JrnlHdr.PostOrder
        FROM JrnlHdr
        WHERE (JrnlHdr.JrnlKey_Journal = 3)
          AND (NOT (JrnlHdr.Description LIKE 'ANULAD%'))
          AND (JrnlHdr.JournalEx = 8) AND (JrnlHdr.JrnlTypeEx = 0)
          AND (JrnlHdr.Reference = ?)
        """;

    private const string SqlIva = """
        SELECT JrnlRow.Amount AS IVAAmount, Tax_Code.ID AS TaxID
        FROM JrnlRow, { oj JrnlHdr LEFT OUTER JOIN Tax_Code ON JrnlHdr.SalesTaxCode = Tax_Code.ID }
        WHERE JrnlHdr.PostOrder = JrnlRow.PostOrder AND (JrnlHdr.JrnlKey_Journal = 3)
          AND (JrnlHdr.JournalEx = 9) AND (JrnlHdr.PurchOrder = '4')
          AND (JrnlRow.LinkToOtherTrxIndex < 0 OR JrnlRow.RowType = 5)
          AND (JrnlHdr.PostOrder = ?)
        """;

    private const string SqlIvaRate = "SELECT Rate1 FROM Tax_Authority WHERE ID = ?";

    private const string SqlLineas = """
        SELECT JrnlRow.Quantity, JrnlRow.Amount, JrnlRow.RowDescription,
               JrnlRow.SalesTaxType, LineItem.ItemID
        FROM { oj JrnlRow LEFT OUTER JOIN LineItem ON JrnlRow.ItemRecordNumber = LineItem.ItemRecordNumber }
        WHERE (JrnlRow.LinkToOtherTrxIndex > 0) AND (JrnlRow.RowType = 0)
          AND (JrnlRow.Amount <> 0) AND (JrnlRow.PostOrder = ?)
        ORDER BY JrnlRow.RowNumber
        """;

    public static async Task<NotaCreditoLeida?> LeerAsync(
        OdbcConnection conexion, string postOrder, CancellationToken cancellationToken = default)
    {
        FilaCabeceraNc cab;
        await using (var cmd = new OdbcCommand(SqlCabecera, conexion))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = postOrder });
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await r.ReadAsync(cancellationToken))
            {
                return null;
            }
            cab = new FilaCabeceraNc(
                Reference: Texto(r, "Reference"),
                MainAmount: Decimal(r, "MainAmount"),
                CustomerId: Texto(r, "CustomerId"),
                TransactionDate: Fecha(r, "TransactionDate"),
                TaxDescription: TextoNull(r, "Tax"),
                ReturnAuthorization: TextoNull(r, "ReturnAuthorization"),
                InvNumber: Texto(r, "InvNumber"));
        }

        // Factura modificada
        var relacion = new RelacionFactura(false, string.Empty, "01", default, Array.Empty<string>());
        string? invPostOrder = null;
        await using (var cmd = new OdbcCommand(SqlFacturaRelacionada, conexion))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = cab.InvNumber });
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await r.ReadAsync(cancellationToken))
            {
                invPostOrder = Texto(r, "PostOrder");
            }
        }
        if (invPostOrder is not null)
        {
            var facturaModificada = await LectorFacturaVenta.LeerAsync(conexion, invPostOrder, cancellationToken);
            if (facturaModificada is not null)
            {
                relacion = new RelacionFactura(
                    Encontrada: true,
                    BillNumber: facturaModificada.Cabecera.NumeroCompleto,
                    BillCodeDoc: "01",
                    DateBill: facturaModificada.Cabecera.FechaEmision ?? default,
                    ErroresFactura: facturaModificada.Errores.Select(e => $"Factura relacionada: {e}").ToList());
            }
        }

        // IVA
        decimal? ivaMonto = null;
        string? ivaTaxId = null;
        await using (var cmd = new OdbcCommand(SqlIva, conexion))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = postOrder });
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await r.ReadAsync(cancellationToken))
            {
                if (!await r.IsDBNullAsync(0, cancellationToken)) ivaMonto = r.GetDecimal(0);
                ivaTaxId = TextoNull(r, "TaxID");
            }
        }

        decimal? ivaRate1 = null;
        if (ivaMonto is not null && !string.IsNullOrEmpty(ivaTaxId))
        {
            await using var cmd = new OdbcCommand(SqlIvaRate, conexion);
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = ivaTaxId });
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await r.ReadAsync(cancellationToken) && !await r.IsDBNullAsync(0, cancellationToken))
            {
                ivaRate1 = r.GetDecimal(0);
            }
        }

        var lineas = new List<FilaLineaNc>();
        await using (var cmd = new OdbcCommand(SqlLineas, conexion))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = postOrder });
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                lineas.Add(new FilaLineaNc(
                    Quantity: Decimal(r, "Quantity"),
                    Amount: Decimal(r, "Amount"),
                    RowDescription: Texto(r, "RowDescription"),
                    SalesTaxType: Texto(r, "SalesTaxType"),
                    ItemId: TextoNull(r, "ItemID")));
            }
        }

        var cliente = await LectorCliente.LeerAsync(conexion, cab.CustomerId, cancellationToken);

        return ArmarDesde(postOrder, cab, relacion, ivaMonto, ivaRate1, lineas, cliente);
    }

    // ---- Filas crudas -----------------------------------------------------

    internal sealed record FilaCabeceraNc(
        string Reference, decimal MainAmount, string CustomerId, DateTime? TransactionDate,
        string? TaxDescription, string? ReturnAuthorization, string InvNumber);

    internal sealed record RelacionFactura(
        bool Encontrada, string BillNumber, string BillCodeDoc, DateTime DateBill,
        IReadOnlyList<string> ErroresFactura);

    internal sealed record FilaLineaNc(
        decimal Quantity, decimal Amount, string RowDescription, string SalesTaxType, string? ItemId);

    // ---- Lógica pura (port de ForceLoadFromPeach) -----------------------

    internal static NotaCreditoLeida ArmarDesde(
        string postOrder,
        FilaCabeceraNc h,
        RelacionFactura relacion,
        decimal? ivaMonto,
        decimal? ivaRate1,
        IReadOnlyList<FilaLineaNc> filasLinea,
        ClienteSri? cliente)
    {
        var errores = new List<string>();

        var numero = NumeroDocumentoSri.AnalizarEstricto(h.Reference); // NC: formato estricto
        if (!numero.EsValido)
        {
            errores.Add("Número de nota de crédito incorrecto.");
        }

        if (cliente is null)
        {
            errores.Add("No se pudo cargar el cliente de la nota de crédito.");
        }

        if (!relacion.Encontrada)
        {
            errores.Add("No se pudo encontrar la factura de venta relacionada.");
        }
        errores.AddRange(relacion.ErroresFactura);

        var cause = string.IsNullOrEmpty(h.ReturnAuthorization) ? "Devolución" : h.ReturnAuthorization!;
        if (cause.Length == 0) cause = "Devolución";

        // --- IVA ---
        double ivaValor = 0;
        var codigoPorcentajeIva = "0";
        double porcentajeIva = 0; // fracción

        if (ivaMonto is not null)
        {
            ivaValor = (double)Math.Abs(Math.Round(ivaMonto.Value, 2));
            var nombre = h.TaxDescription ?? "0";
            codigoPorcentajeIva = nombre.Length > 0 ? nombre[..1] : "0";
            if (ivaRate1 is not null)
            {
                porcentajeIva = (double)(ivaRate1.Value / 100m); // NC: sin redondear (port fiel)
            }
        }

        // --- Líneas ---
        var lineas = new List<FacturaVentaLinea>();
        decimal totalSinImpuestos = 0;
        double baseImponibleIvaTotal = 0;

        foreach (var f in filasLinea)
        {
            var cantidad = (double)Math.Abs(Math.Round(f.Quantity, 2));
            if (cantidad < 1) cantidad = 1;

            var monto = Math.Abs(Math.Round(f.Amount, 2));
            var precioUnitario = (double)Math.Round(monto / (decimal)cantidad, 2);
            var subtotal = (double)monto;
            totalSinImpuestos += monto;

            var codigoPrincipal = string.IsNullOrEmpty(f.ItemId) ? "0" : f.ItemId!;

            double linPct;
            double linBase;
            double linIvaValor;
            string linCodigoPctIva;

            if (f.SalesTaxType == "0")
            {
                linPct = Math.Abs(Math.Round(porcentajeIva, 2));
                linBase = subtotal;
                linIvaValor = (double)Math.Round((decimal)(linBase * linPct), 2);
                linCodigoPctIva = codigoPorcentajeIva;
                baseImponibleIvaTotal += linBase;
            }
            else
            {
                linCodigoPctIva = f.SalesTaxType switch
                {
                    "1" => "0",
                    "5" => "6",
                    "6" => "7",
                    _ => f.SalesTaxType,
                };
                if (linCodigoPctIva == f.SalesTaxType && f.SalesTaxType is not ("0" or "6" or "7"))
                {
                    errores.Add($"Valor incorrecto para (Tax) en detalle: {f.RowDescription}");
                }
                linPct = 0;
                linIvaValor = 0;
                linBase = subtotal;
            }

            lineas.Add(new FacturaVentaLinea
            {
                Descripcion = f.RowDescription,
                Cantidad = cantidad,
                PrecioUnitario = precioUnitario,
                SubtotalSinImpuestos = subtotal,
                BaseImponibleIva = linBase,
                IvaValor = linIvaValor,
                IvaPorcentaje = linPct,
                CodigoPorcentajeIva = linCodigoPctIva,
                CodigoPrincipal = codigoPrincipal,
            });
        }

        if (lineas.Count == 0)
        {
            errores.Add("No se encontró detalles en esta nota de crédito.");
        }

        var totalConImpuestos = Math.Round((double)totalSinImpuestos + ivaValor, 2);

        var cab = new NotaCreditoCabecera
        {
            NumeroCompleto = h.Reference,
            Secuencial = numero.Secuencial,
            CodigoEstablecimiento = numero.CodigoEstablecimiento,
            PuntoEmision = numero.PuntoEmision,
            CustomerRecordNumber = h.CustomerId,
            FechaEmision = h.TransactionDate,
            IvaValor = ivaValor,
            TotalSinImpuestos = (double)totalSinImpuestos,
            TotalConImpuestos = totalConImpuestos,
            BaseImponibleIva = baseImponibleIvaTotal,
            CodigoPorcentajeIva = codigoPorcentajeIva,
            CodigoIva = "2",
        };

        return new NotaCreditoLeida
        {
            PostOrderPeach = postOrder,
            Cabecera = cab,
            DocumentoModificado = new DocumentoModificado(
                relacion.BillNumber, relacion.BillCodeDoc, relacion.DateBill, cause),
            Cliente = cliente,
            Lineas = lineas,
            Errores = errores,
        };
    }

    private static string Texto(DbDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? string.Empty : r.GetValue(i)?.ToString() ?? string.Empty;
    }

    private static string? TextoNull(DbDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetValue(i)?.ToString();
    }

    private static decimal Decimal(DbDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i));
    }

    private static DateTime? Fecha(DbDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : Convert.ToDateTime(r.GetValue(i));
    }
}
