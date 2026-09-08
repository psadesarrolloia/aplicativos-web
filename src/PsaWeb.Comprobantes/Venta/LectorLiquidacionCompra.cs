using System.Data.Common;
using System.Data.Odbc;
using PsaWeb.Comprobantes.Proveedores;
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>
/// Lee una liquidación de compra de Sage 50 por su <c>PostOrder</c> y arma un
/// <see cref="LiquidacionLeida"/>. Port de <c>LoadPurchaseInvoice.ForceLoadFromPeach</c>.
/// El IVA sale de un ítem <c>Category = 'IMPUESTO'</c> cuyo texto de porcentaje se
/// resuelve contra <c>dicTaxRate</c> (vía <see cref="ITasaIvaLookup"/>).
/// La lógica pura está en <see cref="ArmarDesde"/>.
/// </summary>
public static class LectorLiquidacionCompra
{
    private const string SqlCabecera = """
        SELECT JrnlHdr.Reference, JrnlHdr.MainAmount, JrnlHdr.CustVendId,
               JrnlHdr.TransactionDate, JrnlHdr.DateDue
        FROM { oj JrnlHdr LEFT OUTER JOIN Tax_Code ON JrnlHdr.SalesTaxCode = Tax_Code.ID }
        WHERE (JrnlHdr.JrnlKey_Journal = 4)
          AND (NOT (JrnlHdr.Description LIKE 'ANULAD%'))
          AND (JrnlHdr.PostOrder = ?)
        """;

    private const string SqlItemsIva = """
        SELECT JrnlRow.Quantity, JrnlRow.Amount, LineItem.CustomField2, LineItem.CustomField3
        FROM JrnlRow, LineItem
        WHERE (JrnlRow.PostOrder = ?)
          AND JrnlRow.ItemRecordNumber = LineItem.ItemRecordNumber
          AND (LineItem.Category LIKE 'IMPUESTO') AND (JrnlRow.RowNumber > 0)
          AND (JrnlRow.Journal = 4) AND (JrnlRow.RowType = 0)
        """;

    private const string SqlDetalles = """
        SELECT LineItem.ItemID, JrnlRow.Quantity, JrnlRow.Amount, JrnlRow.RowDescription,
               LineItem.CustomField3, LineItem.CustomField4
        FROM JrnlRow, LineItem
        WHERE (JrnlRow.PostOrder = ?)
          AND JrnlRow.ItemRecordNumber = LineItem.ItemRecordNumber
          AND (NOT (LineItem.ItemID = 'AUT-SRI'))
          AND NOT (LineItem.Category LIKE 'R-%RF' OR LineItem.Category LIKE 'R-IVA' OR LineItem.Category LIKE 'IMPUESTO')
          AND (JrnlRow.RowNumber > 0) AND (JrnlRow.Journal = 4) AND (JrnlRow.RowType = 0)
        """;

    public static async Task<LiquidacionLeida?> LeerAsync(
        OdbcConnection conexion,
        string postOrder,
        ITasaIvaLookup tasas,
        CancellationToken cancellationToken = default)
    {
        FilaCabeceraLiq cab;
        await using (var cmd = new OdbcCommand(SqlCabecera, conexion))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = postOrder });
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await r.ReadAsync(cancellationToken))
            {
                return null;
            }
            cab = new FilaCabeceraLiq(
                Reference: Texto(r, "Reference"),
                MainAmount: Dec(r, "MainAmount"),
                CustVendId: Texto(r, "CustVendId"),
                TransactionDate: Fecha(r, "TransactionDate"),
                DateDue: Fecha(r, "DateDue"));
        }

        var itemsIva = new List<FilaItemIva>();
        await using (var cmd = new OdbcCommand(SqlItemsIva, conexion))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = postOrder });
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                itemsIva.Add(new FilaItemIva(
                    Quantity: Dec(r, "Quantity"),
                    Amount: Dec(r, "Amount"),
                    CustomField2: Texto(r, "CustomField2"),
                    CustomField3: Texto(r, "CustomField3")));
            }
        }

        var lineas = new List<FilaLineaLiq>();
        await using (var cmd = new OdbcCommand(SqlDetalles, conexion))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = postOrder });
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                lineas.Add(new FilaLineaLiq(
                    Quantity: Dec(r, "Quantity"),
                    Amount: Dec(r, "Amount"),
                    RowDescription: Texto(r, "RowDescription"),
                    CustomField3: Texto(r, "CustomField3"),
                    CustomField4: Texto(r, "CustomField4")));
            }
        }

        // Resolver la tasa de IVA del ítem IMPUESTO contra dicTaxRate.
        TasaIva? tasa = null;
        var primerImpuesto = itemsIva.FirstOrDefault();
        var textoPorcentaje = TextoPorcentaje(primerImpuesto);
        if (textoPorcentaje is not null)
        {
            tasa = await tasas.BuscarPorNombreAsync(textoPorcentaje, cancellationToken);
        }

        var proveedor = await LectorProveedor.LeerAsync(conexion, cab.CustVendId, cancellationToken);

        return ArmarDesde(postOrder, cab, itemsIva, textoPorcentaje, tasa, lineas, proveedor);
    }

    /// <summary>El texto de porcentaje sale de CustomField3 o, si no termina en "%", de CustomField2.</summary>
    internal static string? TextoPorcentaje(FilaItemIva? item)
    {
        if (item is null) return null;
        if (item.CustomField3.Trim().EndsWith('%')) return item.CustomField3.Trim();
        if (item.CustomField2.Trim().EndsWith('%')) return item.CustomField2.Trim();
        return null; // mal configurado
    }

    // ---- Filas crudas ---------------------------------------------------

    internal sealed record FilaCabeceraLiq(
        string Reference, decimal MainAmount, string CustVendId, DateTime? TransactionDate, DateTime? DateDue);

    internal sealed record FilaItemIva(decimal Quantity, decimal Amount, string CustomField2, string CustomField3);

    internal sealed record FilaLineaLiq(
        decimal Quantity, decimal Amount, string RowDescription, string CustomField3, string CustomField4);

    // ---- Lógica pura --------------------------------------------------

    internal static LiquidacionLeida ArmarDesde(
        string postOrder,
        FilaCabeceraLiq h,
        IReadOnlyList<FilaItemIva> itemsIva,
        string? textoPorcentaje,
        TasaIva? tasa,
        IReadOnlyList<FilaLineaLiq> filasLinea,
        ProveedorSri? proveedor)
    {
        var errores = new List<string>();

        var numero = NumeroDocumentoSri.AnalizarFactura(h.Reference); // liquidación: formato tolerante
        if (!numero.EsValido)
        {
            errores.Add("Número de factura incorrecto.");
        }

        if (proveedor is null)
        {
            errores.Add("No se pudo cargar el proveedor de la liquidación.");
        }
        else
        {
            errores.AddRange(proveedor.Errores);
        }

        if (itemsIva.Count > 1)
        {
            errores.Add("El sistema no cubre liquidaciones de compra con más de un ítem de IVA.");
        }

        var primerItemIva = itemsIva.FirstOrDefault();
        if (primerItemIva is not null && textoPorcentaje is null)
        {
            errores.Add("Ítem de IVA mal configurado: no se encontró el porcentaje en CustomField2 ni CustomField3.");
        }
        if (primerItemIva is not null && textoPorcentaje is not null && tasa is null)
        {
            errores.Add($"No se pudo determinar el código de IVA para '{textoPorcentaje}'.");
        }

        var codigoPorcentajeIva = tasa?.CodigoPorcentaje ?? "4"; // default del .exe
        var porcentajeIva = tasa?.Porcentaje ?? 0;
        var ivaValor = primerItemIva is null
            ? 0d
            : (double)Math.Round(primerItemIva.Amount, 2, MidpointRounding.AwayFromZero);

        // --- Líneas (Amount > 0) ---
        var lineas = new List<FacturaVentaLinea>();
        double totalSinImpuestos = 0;
        double baseImponibleIvaTotal = 0;
        var codigoPorcentajeActual = codigoPorcentajeIva; // se muta como en el .exe (líneas "NO IVA")

        foreach (var f in filasLinea.Where(x => x.Amount > 0))
        {
            var descripcion = f.RowDescription
                .Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

            var cantidad = Math.Round((double)f.Quantity, 2);
            if (cantidad < 1) cantidad = 1;

            var monto = Math.Abs(Math.Round(f.Amount, 2));
            var precioUnitario = (double)Math.Round(monto / (decimal)cantidad, 2);
            var subtotal = (double)monto;
            totalSinImpuestos += subtotal;

            var cf3 = f.CustomField3.ToUpperInvariant();
            string linCodigoPctIva;
            double linPct;

            if (cf3.Contains("IVA") && cf3.Contains("NO"))
            {
                // Port fiel: el .exe muta el código "actual" según CustomField4 aunque
                // la línea quede en "0" — afecta a las líneas siguientes.
                var cf4 = f.CustomField4.Length >= 5 ? f.CustomField4[..5].ToUpperInvariant() : string.Empty;
                codigoPorcentajeActual = cf4 switch
                {
                    "IMPEX" => "7",
                    "NOGRA" => "6",
                    _ => "0",
                };
                linCodigoPctIva = "0";
                linPct = 0;
            }
            else
            {
                linCodigoPctIva = codigoPorcentajeActual;
                linPct = porcentajeIva;
            }

            var baseIva = subtotal;
            var ivaLinea = Math.Round(baseIva * linPct, 2);
            if (linPct > 0)
            {
                baseImponibleIvaTotal += baseIva;
            }

            lineas.Add(new FacturaVentaLinea
            {
                Descripcion = descripcion,
                Cantidad = cantidad,
                PrecioUnitario = precioUnitario,
                SubtotalSinImpuestos = subtotal,
                BaseImponibleIva = baseIva,
                IvaValor = ivaLinea,
                IvaPorcentaje = linPct,
                CodigoPorcentajeIva = linCodigoPctIva,
                CodigoPrincipal = "NoCode",
            });
        }

        if (lineas.Count == 0)
        {
            errores.Add("No se encontró detalles en esta liquidación.");
        }

        var totalSinImp = Math.Round(totalSinImpuestos, 2);
        var totalConImp = Math.Round(totalSinImp + ivaValor, 2);

        var cab = new LiquidacionCabecera
        {
            NumeroCompleto = numero.EsValido ? numero.Completo : h.Reference,
            Secuencial = numero.Secuencial,
            CodigoEstablecimiento = numero.CodigoEstablecimiento,
            PuntoEmision = numero.PuntoEmision,
            VendorRecordNumber = h.CustVendId,
            FechaEmision = h.TransactionDate,
            FechaVencimiento = h.DateDue,
            IvaValor = ivaValor,
            TotalSinImpuestos = totalSinImp,
            TotalConImpuestos = totalConImp,
            BaseImponibleIva = Math.Round(baseImponibleIvaTotal, 2),
            CodigoPorcentajeIva = codigoPorcentajeActual,
            CodigoIva = "2",
        };

        return new LiquidacionLeida
        {
            PostOrderPeach = postOrder,
            Cabecera = cab,
            Proveedor = proveedor,
            Lineas = lineas,
            Errores = errores,
        };
    }

    private static string Texto(DbDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? string.Empty : r.GetValue(i)?.ToString() ?? string.Empty;
    }

    private static decimal Dec(DbDataReader r, string col)
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
