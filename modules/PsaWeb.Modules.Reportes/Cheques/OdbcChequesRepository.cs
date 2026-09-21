using System.Data.Odbc;
using PsaWeb.Modules.Reportes.Comun;

namespace PsaWeb.Modules.Reportes.Cheques;

/// <summary>
/// Acceso de solo lectura a los pagos de Sage 50 (cheques y otros egresos) y a sus asientos, para imprimir el
/// cheque y el comprobante de egreso. Se ejecuta contra la empresa de sesión (o la de configuración si no hay shell).
/// </summary>
public interface IChequesRepository
{
    /// <summary>Pagos del rango de fechas (con número numérico), del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<PagoCheque>> ListarAsync(FiltroCheques filtro, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pagos con su asiento, sólo de los <paramref name="postOrders"/> pedidos y sólo si son pagos de verdad (se
    /// vuelve a exigir diario 2 / tipo 5, para que un valor manipulado no imprima cualquier asiento).
    /// </summary>
    Task<IReadOnlyList<PagoConDetalle>> DetallesAsync(
        IReadOnlyList<long> postOrders, CancellationToken cancellationToken = default);

    /// <summary>Igual que <see cref="DetallesAsync"/> pero contra una empresa explícita por RUC (endpoint de impresión).</summary>
    Task<IReadOnlyList<PagoConDetalle>> DetallesParaRucAsync(
        string ruc, IReadOnlyList<long> postOrders, CancellationToken cancellationToken = default);
}

/// <summary>
/// Consultas reales de Sage 50 (<c>PaymentsCreateQuery</c> / <c>PaymentsCreateDetailsQuery</c> del .mdb): pagos
/// (diario 2, tipo 5) por rango de fechas, y por cada uno su asiento con la cuenta y la factura pagada. El detalle
/// de N+1 (una consulta por pago) se colapsa en una por lote con <c>IN</c>. Los valores van por parámetro <c>?</c>
/// (Access concatenaba la fecha con formato <c>{ d 'yyyy-mm-dd' }</c>).
/// </summary>
internal sealed class OdbcChequesRepository(SageAcceso sage) : IChequesRepository
{
    private const string ColumnasPago = """
        SELECT JrnlHdr.PostOrder, JrnlHdr.TransactionDate, JrnlHdr.TrxName, JrnlHdr.Reference, JrnlHdr.MainAmount
        FROM JrnlHdr
        WHERE (JrnlHdr.JrnlKey_Journal = 2)
          AND (JrnlHdr.JournalEx = 5)
          AND (JrnlHdr.JrnlTypeEx = 0)
        """;

    private const string SqlPorFechas = ColumnasPago + """

          AND (JrnlHdr.TransactionDate BETWEEN ? AND ?)
        ORDER BY JrnlHdr.TransactionDate DESC, JrnlHdr.PostOrder DESC
        """;

    private const string SqlPorPostOrder = ColumnasPago + """

          AND (JrnlHdr.PostOrder IN (@@IN@@))
        ORDER BY JrnlHdr.TransactionDate DESC, JrnlHdr.PostOrder DESC
        """;

    // OJO: contiene llaves de la sintaxis { oj ... } del driver; por eso se reemplaza @@IN@@ y NO se usa string.Format.
    private const string SqlDetalle = """
        SELECT JrnlRow.PostOrder, JrnlRow.RowNumber, Chart.AccountID, Chart.AccountDescription,
               JrnlRow.RowDescription, JrnlRow.Amount, Bills.Reference AS BillNum, Bills.Description AS BillVendor
        FROM Chart, { oj JrnlRow LEFT OUTER JOIN JrnlHdr Bills ON JrnlRow.LinkToAnotherTrx = Bills.PostOrder }
        WHERE JrnlRow.GLAcntNumber = Chart.GLAcntNumber AND (JrnlRow.PostOrder IN (@@IN@@))
        ORDER BY JrnlRow.PostOrder, JrnlRow.RowNumber
        """;

    public async Task<IReadOnlyList<PagoCheque>> ListarAsync(FiltroCheques filtro, CancellationToken cancellationToken = default)
    {
        await using var cn = await sage.AbrirSesionAsync(cancellationToken);
        await using var cmd = new OdbcCommand(SqlPorFechas, cn);
        SageAcceso.AgregarFecha(cmd, filtro.Desde.ToDateTime(TimeOnly.MinValue));
        SageAcceso.AgregarFecha(cmd, filtro.Hasta.ToDateTime(TimeOnly.MinValue));
        return (await LeerPagosAsync(cmd, cancellationToken)).Where(filtro.Coincide).ToList();
    }

    public async Task<IReadOnlyList<PagoConDetalle>> DetallesAsync(
        IReadOnlyList<long> postOrders, CancellationToken cancellationToken = default)
    {
        await using var cn = await sage.AbrirSesionAsync(cancellationToken);
        return await LeerDetallesAsync(cn, postOrders, cancellationToken);
    }

    public async Task<IReadOnlyList<PagoConDetalle>> DetallesParaRucAsync(
        string ruc, IReadOnlyList<long> postOrders, CancellationToken cancellationToken = default)
    {
        await using var cn = await sage.AbrirParaRucAsync(ruc, cancellationToken);
        return await LeerDetallesAsync(cn, postOrders, cancellationToken);
    }

    private static async Task<IReadOnlyList<PagoConDetalle>> LeerDetallesAsync(
        OdbcConnection cn, IReadOnlyList<long> postOrders, CancellationToken ct)
    {
        var pedidos = postOrders.Distinct().ToList();
        var pagos = new List<PagoCheque>();
        var lineas = new Dictionary<long, List<LineaPago>>();

        foreach (var lote in SageAcceso.Lotes(pedidos))
        {
            await using (var cmd = new OdbcCommand(SqlPorPostOrder.Replace("@@IN@@", SageAcceso.Marcadores(lote.Count)), cn))
            {
                SageAcceso.AgregarEnteros(cmd, lote);
                pagos.AddRange(await LeerPagosAsync(cmd, ct));
            }

            await using (var cmd = new OdbcCommand(SqlDetalle.Replace("@@IN@@", SageAcceso.Marcadores(lote.Count)), cn))
            {
                SageAcceso.AgregarEnteros(cmd, lote);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var po = Convert.ToInt64(r.GetValue(0));
                    if (!lineas.TryGetValue(po, out var lista))
                    {
                        lineas[po] = lista = new List<LineaPago>();
                    }
                    // RowAmount = Abs(Round(Amount, 2)) y «Amount» con signo, como el reporte de Access.
                    var importe = Math.Round(SageAcceso.Decimal(r, 5) ?? 0m, 2, MidpointRounding.ToEven);
                    lista.Add(new LineaPago(
                        NumeroFila: r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1)),
                        CuentaId: SageAcceso.Texto(r, 2),
                        CuentaDescripcion: SageAcceso.Texto(r, 3),
                        Descripcion: SageAcceso.Texto(r, 4),
                        Importe: importe,
                        Factura: SageAcceso.Texto(r, 6),
                        ProveedorFactura: SageAcceso.Texto(r, 7)));
                }
            }
        }

        // Se respeta el orden pedido (el de la lista de la pantalla).
        var porPostOrder = pagos.ToDictionary(p => p.PostOrder);
        return pedidos
            .Where(porPostOrder.ContainsKey)
            .Select(po => new PagoConDetalle(porPostOrder[po], lineas.TryGetValue(po, out var l) ? l : new List<LineaPago>()))
            .ToList();
    }

    private static async Task<List<PagoCheque>> LeerPagosAsync(OdbcCommand cmd, CancellationToken ct)
    {
        var lista = new List<PagoCheque>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var fecha = SageAcceso.Fecha(r, 1);
            var referencia = ReferenciaPago.Analizar(SageAcceso.Texto(r, 3));
            if (fecha is null || referencia is null)
            {
                continue; // sin número de cheque (o sin fecha): el reporte de Access no lo lista
            }
            lista.Add(new PagoCheque(
                PostOrder: Convert.ToInt64(r.GetValue(0)),
                Fecha: DateOnly.FromDateTime(fecha.Value),
                Beneficiario: SageAcceso.Texto(r, 2),
                Referencia: referencia,
                // MainAmount = Abs(Round(MainAmount, 2))
                Monto: Math.Abs(Math.Round(SageAcceso.Decimal(r, 4) ?? 0m, 2, MidpointRounding.ToEven))));
        }
        return lista;
    }
}
