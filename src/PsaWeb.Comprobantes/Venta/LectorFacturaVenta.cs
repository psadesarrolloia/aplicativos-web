using System.Data.Common;
using System.Data.Odbc;
using PsaWeb.Comprobantes.Clientes;
using PsaWeb.Comprobantes.Sri;
using PsaWeb.Datil.Model;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>
/// Lee una factura de venta de Sage 50 por su <c>PostOrder</c> (cabecera de
/// <c>JrnlHdr</c>, descuentos y detalle de <c>JrnlRow</c>, tasa de IVA de
/// <c>Tax_Code</c>/<c>Tax_Authority</c>) y arma un <see cref="FacturaVentaLeida"/>.
/// Port de <c>LoadSaleInvoice.ForceLoadFromPeach</c>. La lógica de descuentos e
/// IVA está en <see cref="ArmarDesde"/> (pura, testeable sin ODBC).
/// </summary>
public static class LectorFacturaVenta
{
    private const string SqlCabecera = """
        SELECT JrnlHdr.Reference, JrnlHdr.MainAmount, JrnlHdr.CustVendId AS CustomerId,
               JrnlHdr.TransactionDate, JrnlHdr.DateDue,
               Tax_Code.Description AS Tax, Tax_Code.ID AS TaxID
        FROM { oj JrnlHdr LEFT OUTER JOIN Tax_Code ON JrnlHdr.SalesTaxCode = Tax_Code.ID }
        WHERE (JrnlHdr.JrnlKey_Journal = 3)
          AND (NOT (JrnlHdr.Description LIKE 'ANULAD%'))
          AND (JrnlHdr.JournalEx = 8) AND (JrnlHdr.JrnlTypeEx = 0)
          AND (JrnlHdr.PostOrder = ?)
        """;

    private const string SqlDescuento = """
        SELECT Amount AS Discount, SalesTaxType
        FROM JrnlRow
        WHERE (PostOrder = ?) AND (RowNumber > 0) AND (JournalRowEx = 9)
          AND (RowType = 0) AND (Amount > 0)
        """;

    private const string SqlIva = """
        SELECT Amount
        FROM JrnlRow
        WHERE (PostOrder = ?) AND (Journal = 3) AND (RowType = 5)
        """;

    private const string SqlIvaRate = """
        SELECT Rate1 FROM Tax_Authority
        WHERE ID IN (SELECT TaxAuthority1 FROM Tax_Code WHERE Description = ?)
        """;

    private const string SqlLineas = """
        SELECT JrnlRow.Quantity, JrnlRow.Amount, JrnlRow.RowDescription,
               JrnlRow.SalesTaxType, LineItem.ItemID
        FROM { oj JrnlRow LEFT OUTER JOIN LineItem ON JrnlRow.ItemRecordNumber = LineItem.ItemRecordNumber }
        WHERE (JrnlRow.PostOrder = ?) AND (JrnlRow.RowNumber > 0)
          AND (JrnlRow.RowType = 0) AND (JrnlRow.Amount < 0)
        ORDER BY JrnlRow.RowNumber
        """;

    public static async Task<FacturaVentaLeida?> LeerAsync(
        OdbcConnection conexion,
        string postOrder,
        CancellationToken cancellationToken = default,
        IReadOnlyList<InfoAdicionalItem>? infoAdicional = null)
    {
        FilaCabecera cabecera;
        await using (var cmd = new OdbcCommand(SqlCabecera, conexion))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = postOrder });
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await r.ReadAsync(cancellationToken))
            {
                return null;
            }
            cabecera = new FilaCabecera(
                Reference: Texto(r, "Reference"),
                MainAmount: Decimal(r, "MainAmount"),
                CustomerId: Texto(r, "CustomerId"),
                TransactionDate: Fecha(r, "TransactionDate"),
                DateDue: Fecha(r, "DateDue"),
                TaxId: TextoNull(r, "TaxID"),
                TaxDescription: TextoNull(r, "Tax"));
        }

        FilaDescuento? descuento = null;
        await using (var cmd = new OdbcCommand(SqlDescuento, conexion))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = postOrder });
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await r.ReadAsync(cancellationToken))
            {
                descuento = new FilaDescuento(Decimal(r, "Discount"), Entero(r, "SalesTaxType"));
            }
        }

        decimal? ivaMonto = null;
        await using (var cmd = new OdbcCommand(SqlIva, conexion))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = postOrder });
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await r.ReadAsync(cancellationToken) && !await r.IsDBNullAsync(0, cancellationToken))
            {
                ivaMonto = r.GetDecimal(0);
            }
        }

        decimal? ivaRate1 = null;
        if (ivaMonto is not null && !string.IsNullOrEmpty(cabecera.TaxId) && !string.IsNullOrEmpty(cabecera.TaxDescription))
        {
            await using var cmd = new OdbcCommand(SqlIvaRate, conexion);
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = cabecera.TaxDescription });
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await r.ReadAsync(cancellationToken) && !await r.IsDBNullAsync(0, cancellationToken))
            {
                ivaRate1 = r.GetDecimal(0);
            }
        }

        var lineas = new List<FilaLinea>();
        await using (var cmd = new OdbcCommand(SqlLineas, conexion))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = postOrder });
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                lineas.Add(new FilaLinea(
                    Quantity: Decimal(r, "Quantity"),
                    Amount: Decimal(r, "Amount"),
                    RowDescription: Texto(r, "RowDescription"),
                    SalesTaxType: Texto(r, "SalesTaxType"),
                    ItemId: TextoNull(r, "ItemID")));
            }
        }

        var cliente = await LectorCliente.LeerAsync(conexion, cabecera.CustomerId, cancellationToken);

        return ArmarDesde(postOrder, cabecera, descuento, ivaMonto, ivaRate1, lineas, cliente,
            infoAdicional ?? Array.Empty<InfoAdicionalItem>());
    }

    // ---- Filas crudas (entrada de ArmarDesde) --------------------------------

    internal sealed record FilaCabecera(
        string Reference, decimal MainAmount, string CustomerId,
        DateTime? TransactionDate, DateTime? DateDue, string? TaxId, string? TaxDescription);

    internal sealed record FilaDescuento(decimal Amount, int SalesTaxType);

    internal sealed record FilaLinea(
        decimal Quantity, decimal Amount, string RowDescription, string SalesTaxType, string? ItemId);

    // ---- Lógica pura (port de ForceLoadFromPeach) ---------------------------

    internal static FacturaVentaLeida ArmarDesde(
        string postOrder,
        FilaCabecera h,
        FilaDescuento? descuento,
        decimal? ivaMonto,
        decimal? ivaRate1,
        IReadOnlyList<FilaLinea> filasLinea,
        ClienteSri? cliente,
        IReadOnlyList<InfoAdicionalItem> infoAdicional)
    {
        var errores = new List<string>();

        // --- Número de factura ---
        var numero = NumeroDocumentoSri.AnalizarFactura(h.Reference);
        if (!numero.EsValido)
        {
            errores.Add("Número de factura incorrecto.");
        }

        // --- Cliente ---
        if (cliente is null)
        {
            errores.Add("No se pudo cargar el cliente de la factura.");
        }

        // --- Descuentos (una sola fila, como el .exe) ---
        double descuentoConIva = 0;
        double descuentoSinIva = 0;
        if (descuento is not null)
        {
            var valor = (double)Math.Abs(Math.Round(descuento.Amount, 2));
            if (descuento.SalesTaxType == 0) descuentoConIva += valor;
            else descuentoSinIva += valor;
        }

        // --- IVA de cabecera ---
        var totalConImpuestos = (double)Math.Abs(Math.Round(h.MainAmount, 2));
        double ivaValor = 0;
        var codigoPorcentajeIva = "0";
        double porcentajeIva = 0; // fracción (0.12 / 0.15)

        if (ivaMonto is not null)
        {
            ivaValor = (double)Math.Abs(Math.Round(ivaMonto.Value, 2));

            if (!string.IsNullOrEmpty(h.TaxId))
            {
                var nombre = h.TaxDescription ?? string.Empty;
                codigoPorcentajeIva = nombre.Length < 2 ? "0" : nombre[..1];
                if (ivaRate1 is not null)
                {
                    porcentajeIva = (double)Math.Round(ivaRate1.Value / 100m, 2);
                }
            }
        }

        if (ivaValor == 0)
        {
            porcentajeIva = 0;
        }

        var totalSinImpuestos = Math.Round(totalConImpuestos - ivaValor, 2);

        // --- Líneas ---
        var lineas = new List<FacturaVentaLinea>();
        double baseImponibleIvaTotal = 0;
        double descuentoTotal = 0;

        foreach (var f in filasLinea)
        {
            var descripcion = f.RowDescription
                .Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

            var cantidad = (double)Math.Abs(Math.Round(f.Quantity, 2));
            if (cantidad < 1) cantidad = 1;

            var monto = Math.Abs(Math.Round(f.Amount, 2)); // decimal
            var precioUnitario = (double)Math.Round(monto / (decimal)cantidad, 2);
            var subtotal = (double)monto;

            var codigoPrincipal = string.IsNullOrEmpty(f.ItemId) ? "0" : f.ItemId!;

            double linIvaPorcentaje;
            double linBaseIva;
            double linIvaValor;
            string linCodigoPctIva;

            if (f.SalesTaxType == "0")
            {
                linIvaPorcentaje = Math.Abs(Math.Round(porcentajeIva, 2));
                linBaseIva = subtotal;
                linIvaValor = Math.Round(linBaseIva * linIvaPorcentaje, 2);
                linCodigoPctIva = codigoPorcentajeIva;
                baseImponibleIvaTotal += linBaseIva;
            }
            else
            {
                linCodigoPctIva = f.SalesTaxType switch
                {
                    "1" => "0",
                    "5" => "6",
                    "6" => "7",
                    _ => "0",
                };
                linIvaPorcentaje = 0;
                linIvaValor = 0;
                linBaseIva = subtotal;
            }

            var descuentoLinea = 0d;

            if (subtotal > 0)
            {
                if (descuentoSinIva > 0 || descuentoConIva > 0)
                {
                    if (linIvaPorcentaje > 0)
                    {
                        if (descuentoConIva < subtotal)
                        {
                            subtotal = Math.Round(subtotal - descuentoConIva, 2);
                            // Port fiel: usa la base ANTES de reasignarla.
                            precioUnitario = Math.Round(linBaseIva / cantidad, 2);
                            linBaseIva = subtotal;
                            linIvaValor = Math.Round(linBaseIva * linIvaPorcentaje, 2);
                            descuentoLinea = descuentoConIva;
                            descuentoTotal = descuentoConIva;
                            baseImponibleIvaTotal = Math.Round(baseImponibleIvaTotal - descuentoConIva, 2);
                            descuentoConIva = 0;
                        }
                    }
                    else
                    {
                        if (descuentoSinIva < subtotal)
                        {
                            subtotal -= descuentoSinIva;
                            precioUnitario = Math.Round(subtotal / cantidad, 2);
                            linBaseIva = subtotal;
                            // Port fiel del .exe: multiplica por IVAValue (0 en líneas sin IVA), no por el %.
                            linIvaValor = Math.Round(linBaseIva * linIvaValor, 2);
                            descuentoLinea = descuentoSinIva;
                            descuentoTotal = descuentoSinIva;
                            descuentoSinIva = 0;
                        }
                    }
                }

                lineas.Add(new FacturaVentaLinea
                {
                    Descripcion = descripcion,
                    Cantidad = cantidad,
                    PrecioUnitario = precioUnitario,
                    SubtotalSinImpuestos = subtotal,
                    BaseImponibleIva = linBaseIva,
                    IvaValor = linIvaValor,
                    IvaPorcentaje = linIvaPorcentaje,
                    CodigoPorcentajeIva = linCodigoPctIva,
                    CodigoPrincipal = codigoPrincipal,
                    Descuento = descuentoLinea,
                });
            }
        }

        // --- Verificaciones (región "Verificaciones" del .exe) ---
        if (lineas.Count == 0)
        {
            errores.Add("No se encontró detalles en esta factura.");
        }
        else if (descuentoSinIva != 0 || descuentoConIva != 0)
        {
            errores.Add("No se pudo calcular correctamente el descuento.");
        }

        var cab = new FacturaVentaCabecera
        {
            NumeroCompleto = numero.EsValido ? numero.Completo : h.Reference,
            Secuencial = numero.Secuencial,
            CodigoEstablecimiento = numero.CodigoEstablecimiento,
            PuntoEmision = numero.PuntoEmision,
            CustomerRecordNumber = h.CustomerId,
            FechaEmision = h.TransactionDate,
            FechaVencimiento = h.DateDue,
            TotalConImpuestos = totalConImpuestos,
            IvaValor = ivaValor,
            TotalSinImpuestos = totalSinImpuestos,
            BaseImponibleIva = baseImponibleIvaTotal,
            DescuentoTotal = descuentoTotal,
            CodigoPorcentajeIva = codigoPorcentajeIva,
            CodigoIva = "2",
        };

        return new FacturaVentaLeida
        {
            PostOrderPeach = postOrder,
            Cabecera = cab,
            Cliente = cliente,
            Lineas = lineas,
            InfoAdicional = infoAdicional,
            Errores = errores,
        };
    }

    // ---- helpers de lectura -----------------------------------------------

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

    private static int Entero(DbDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
    }

    private static DateTime? Fecha(DbDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : Convert.ToDateTime(r.GetValue(i));
    }
}
