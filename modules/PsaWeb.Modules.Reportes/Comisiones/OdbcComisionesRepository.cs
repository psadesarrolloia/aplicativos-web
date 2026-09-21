using System.Data.Odbc;
using System.Text;
using PsaWeb.Modules.Reportes.Comun;
using PsaWeb.Modules.Reportes.Pwc;

namespace PsaWeb.Modules.Reportes.Comisiones;

/// <summary>
/// Acceso de solo lectura al reporte de Comisiones de Access: facturas de venta cobradas por recibos de cobro
/// dentro de un rango de recibos (o de fechas), con sus retenciones y cruces. Se ejecuta contra la empresa de
/// sesión (o la de configuración si no hay shell).
/// </summary>
public interface IComisionesRepository
{
    Task<ResultadoComisiones> GenerarAsync(FiltroComisiones filtro, CancellationToken cancellationToken = default);

    /// <summary>
    /// Igual que <see cref="GenerarAsync"/> pero contra una empresa explícita por RUC (endpoint de
    /// exportación, que no tiene el contexto del circuito Blazor).
    /// </summary>
    Task<ResultadoComisiones> GenerarParaRucAsync(string ruc, FiltroComisiones filtro, CancellationToken cancellationToken = default);
}

/// <summary>
/// Consultas reales de Sage 50. Son las de <c>Sage50connection</c> del .mdb
/// (<c>SalesInvoicesForComissions</c>, <c>SalesInvoicesTaxSubTForPWCQuery</c>, <c>SalesInvoicesTwhForComissions</c>,
/// <c>SalesInvoicesForCJForComissions</c>) con valores por parámetro <c>?</c> (Access los concatenaba en el
/// SQL), un desempate por <c>PostOrder</c> en el <c>ORDER BY</c> y las 3 consultas por fila (N+1) colapsadas en una por
/// lote de facturas.
/// </summary>
internal sealed class OdbcComisionesRepository(SageAcceso sage) : IComisionesRepository
{
    // Consulta principal. Los acotadores (rango de recibos como TEXTO —bug C2—, fechas del recibo) se agregan
    // más abajo con marcadores; nunca se interpolan valores.
    private const string SqlPrincipalInicio = """
        SELECT SaleInv.PostOrder, Receipt.CustVendId AS CustVendId,
               Customers.Customer_Bill_Name + Customers.CustomField1 AS TrxName,
               SaleInv.Reference AS NumFact, SaleInv.TransactionDate AS Fecha,
               SaleInv.MainAmount AS TOTAL, SaleInv.MainAmount - SaleInv.AmountPaid AS SALDO,
               SaleInv.AmountPaid AS PAID, Receipt.Reference AS ReceiptNum,
               Receipt.TransactionDate AS ReceiptDate, JrnlRow.Amount, SaleInv.ShipToZIP
        FROM JrnlHdr Receipt, JrnlRow, JrnlHdr SaleInv, Customers
        WHERE Receipt.PostOrder = JrnlRow.PostOrder
          AND SaleInv.CustVendId = Customers.CustomerRecordNumber
          AND JrnlRow.LinkToAnotherTrx = SaleInv.PostOrder
          AND (Receipt.JournalEx = 3)
          AND (Receipt.JrnlKey_Journal = 1)
          AND (NOT (Receipt.Reference LIKE '%-%'))
        """;

    private const string SqlPrincipalFin = """
          AND (NOT (SaleInv.Description LIKE 'ANULAD%'))
        ORDER BY Receipt.TrxName, Fecha, Receipt.TransactionDate DESC, SaleInv.PostOrder
        """;

    private const string SqlGruposFmt = """
        SELECT PostOrder, RowType, TaxAuthorityCode, SUM(Amount) AS Subt
        FROM JrnlRow
        WHERE (RowNumber <> 0) AND (PostOrder IN ({0}))
        GROUP BY PostOrder, RowType, TaxAuthorityCode
        ORDER BY PostOrder, RowType, TaxAuthorityCode
        """;

    private const string SqlRetencionesFmt = """
        SELECT JrnlRow.LinkToAnotherTrx, SUM(JrnlRow.Amount) AS Twh
        FROM JrnlHdr CreditMemo, JrnlRow, Jobs
        WHERE CreditMemo.PostOrder = JrnlRow.PostOrder
          AND JrnlRow.JobRecordNumber = Jobs.JobRecordNumber
          AND (CreditMemo.JrnlKey_Journal = 3)
          AND (CreditMemo.JournalEx = 9)
          AND (CreditMemo.JrnlTypeEx = 2)
          AND (JrnlRow.RowType = 0)
          AND (CreditMemo.PurchOrder LIKE '4R')
          AND (JrnlRow.LinkToAnotherTrx IN ({0}))
        GROUP BY JrnlRow.LinkToAnotherTrx
        """;

    private const string SqlCrucesFmt = """
        SELECT JrnlRow.LinkToAnotherTrx, SUM(JrnlRow.Amount) AS CRUCE
        FROM JrnlRow, JrnlHdr
        WHERE JrnlRow.PostOrder = JrnlHdr.PostOrder
          AND (JrnlHdr.Reference LIKE 'CJ%')
          AND (JrnlRow.RowType = 0)
          AND (JrnlRow.LinkToAnotherTrx IN ({0}))
        GROUP BY JrnlRow.LinkToAnotherTrx
        """;

    public async Task<ResultadoComisiones> GenerarAsync(FiltroComisiones filtro, CancellationToken cancellationToken = default)
    {
        await using var cn = await sage.AbrirSesionAsync(cancellationToken);
        return await EjecutarAsync(cn, filtro, cancellationToken);
    }

    public async Task<ResultadoComisiones> GenerarParaRucAsync(string ruc, FiltroComisiones filtro, CancellationToken cancellationToken = default)
    {
        await using var cn = await sage.AbrirParaRucAsync(ruc, cancellationToken);
        return await EjecutarAsync(cn, filtro, cancellationToken);
    }

    private static async Task<ResultadoComisiones> EjecutarAsync(OdbcConnection cn, FiltroComisiones filtro, CancellationToken ct)
    {
        if (!filtro.TieneAcotador)
        {
            throw new ArgumentException("Indique un rango de recibos o de fechas del recibo.", nameof(filtro));
        }

        var cobros = (await LeerCobrosAsync(cn, filtro, ct)).Where(filtro.Coincide).ToList();
        if (cobros.Count == 0)
        {
            return ResultadoComisiones.Vacio;
        }

        var facturas = cobros.Select(c => c.PostOrderFactura).Distinct().ToList();
        var grupos = new List<GrupoImpuestoCrudo>();
        var retenciones = new Dictionary<long, decimal>();
        var cruces = new Dictionary<long, decimal>();
        foreach (var lote in SageAcceso.Lotes(facturas))
        {
            grupos.AddRange(await LeerGruposAsync(cn, lote, ct));
            foreach (var (po, v) in await LeerSumasAsync(cn, SqlRetencionesFmt, lote, ct)) retenciones[po] = v;
            foreach (var (po, v) in await LeerSumasAsync(cn, SqlCrucesFmt, lote, ct)) cruces[po] = v;
        }

        return ArmadorComisiones.Armar(cobros, grupos, retenciones, cruces, filtro.AbonoPorRecibo);
    }

    private static async Task<List<CobroCrudo>> LeerCobrosAsync(OdbcConnection cn, FiltroComisiones filtro, CancellationToken ct)
    {
        var sql = new StringBuilder(SqlPrincipalInicio);
        var parametros = new List<OdbcParameter>();

        // C2 (fiel): el rango de recibos se compara como TEXTO en el SQL, igual que Access. Con
        // RangoNumerico se lee sin acotar por número y se filtra como número en memoria.
        if (filtro.TieneRangoRecibos && !filtro.RangoNumerico)
        {
            sql.AppendLine().Append("  AND (Receipt.Reference <= ?)").AppendLine().Append("  AND (Receipt.Reference >= ?)");
            parametros.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = filtro.ReciboHasta!.Trim() });
            parametros.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = FiltroComisiones.FormatearDesde(filtro.ReciboDesde!) });
        }
        if (filtro.FechaReciboDesde is { } desde)
        {
            sql.AppendLine().Append("  AND (Receipt.TransactionDate >= ?)");
            parametros.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = desde.ToDateTime(TimeOnly.MinValue) });
        }
        if (filtro.FechaReciboHasta is { } hasta)
        {
            sql.AppendLine().Append("  AND (Receipt.TransactionDate <= ?)");
            parametros.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = hasta.ToDateTime(TimeOnly.MinValue) });
        }
        sql.AppendLine().Append(SqlPrincipalFin);

        await using var cmd = new OdbcCommand(sql.ToString(), cn);
        foreach (var p in parametros)
        {
            cmd.Parameters.Add(p);
        }

        var lista = new List<CobroCrudo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var fecha = SageAcceso.Fecha(r, 4);
            var fechaRecibo = SageAcceso.Fecha(r, 9);
            if (fecha is null || fechaRecibo is null || r.IsDBNull(1))
            {
                continue; // el .exe también omitía las filas sin cliente (CustVendId nulo)
            }
            lista.Add(new CobroCrudo(
                PostOrderFactura: Convert.ToInt64(r.GetValue(0)),
                ClienteId: Convert.ToInt64(r.GetValue(1)),
                Cliente: SageAcceso.Texto(r, 2),
                Factura: SageAcceso.Texto(r, 3),
                Fecha: DateOnly.FromDateTime(fecha.Value),
                Total: SageAcceso.Decimal(r, 5),
                Saldo: SageAcceso.Decimal(r, 6),
                Pagado: SageAcceso.Decimal(r, 7),
                Recibo: SageAcceso.Texto(r, 8),
                FechaRecibo: DateOnly.FromDateTime(fechaRecibo.Value),
                ImporteRecibo: SageAcceso.Decimal(r, 10) ?? 0m,
                Ciudad: SageAcceso.Texto(r, 11)));
        }
        return lista;
    }

    private static async Task<List<GrupoImpuestoCrudo>> LeerGruposAsync(
        OdbcConnection cn, IReadOnlyList<long> postOrders, CancellationToken ct)
    {
        await using var cmd = new OdbcCommand(string.Format(SqlGruposFmt, SageAcceso.Marcadores(postOrders.Count)), cn);
        SageAcceso.AgregarEnteros(cmd, postOrders);

        var lista = new List<GrupoImpuestoCrudo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            lista.Add(new GrupoImpuestoCrudo(
                Convert.ToInt64(r.GetValue(0)),
                r.IsDBNull(1) ? 0 : Convert.ToInt64(r.GetValue(1)),
                SageAcceso.Texto(r, 2),
                SageAcceso.Decimal(r, 3) ?? 0m));
        }
        return lista;
    }

    private static async Task<List<(long PostOrder, decimal Valor)>> LeerSumasAsync(
        OdbcConnection cn, string sqlFmt, IReadOnlyList<long> postOrders, CancellationToken ct)
    {
        await using var cmd = new OdbcCommand(string.Format(sqlFmt, SageAcceso.Marcadores(postOrders.Count)), cn);
        SageAcceso.AgregarEnteros(cmd, postOrders);

        var lista = new List<(long, decimal)>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            if (r.IsDBNull(0) || r.IsDBNull(1))
            {
                continue; // SUM sin filas = NULL = 0 (Access: IsNull → 0)
            }
            lista.Add((Convert.ToInt64(r.GetValue(0)), SageAcceso.Decimal(r, 1) ?? 0m));
        }
        return lista;
    }
}
