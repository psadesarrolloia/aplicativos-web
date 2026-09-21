using System.Data.Odbc;
using PsaWeb.Modules.Reportes.Comun;

namespace PsaWeb.Modules.Reportes.Pwc;

/// <summary>
/// Acceso de solo lectura al reporte «CXC PWC» de Access: facturas de venta con saldo, con subtotal, IVA
/// y retenciones. Se ejecuta contra la empresa de sesión (o la de configuración si no hay shell).
/// </summary>
public interface IPwcRepository
{
    /// <summary>Ciudades y clientes con saldo, para los selectores de filtro.</summary>
    Task<OpcionesPwc> OpcionesAsync(CancellationToken cancellationToken = default);

    Task<ResultadoPwc> GenerarAsync(FiltroPwc filtro, CancellationToken cancellationToken = default);

    /// <summary>
    /// Igual que <see cref="GenerarAsync"/> pero contra una empresa explícita por RUC (endpoint de
    /// exportación, que no tiene el contexto del circuito Blazor).
    /// </summary>
    Task<ResultadoPwc> GenerarParaRucAsync(string ruc, FiltroPwc filtro, CancellationToken cancellationToken = default);
}

/// <summary>
/// Consultas reales de Sage 50 (Pervasive vía ODBC). Son las de <c>Sage50connection</c> del .mdb
/// (<c>SalesInvoicesForPWCQuery</c>, <c>SalesInvoicesTaxSubTForPWCQuery</c>,
/// <c>SalesInvoicesTwhForPWCQuery</c>), con dos cambios que no alteran valores: se agrega un
/// <c>ORDER BY</c> (Access no tenía y devolvía el orden físico) y las 2 consultas por factura (N+1) se
/// colapsan en una por lote de facturas con <c>IN</c>.
/// </summary>
internal sealed class OdbcPwcRepository(SageAcceso sage) : IPwcRepository
{
    // Cabecera: facturas de venta (diario 3, tipo 8, no anuladas) con saldo. Cliente = nombre + campo
    // personalizado 1 SIN separador (Sage parte los nombres largos en 2 campos).
    private const string SqlCabecera = """
        SELECT JrnlHdr.PostOrder, Customers.Customer_Bill_Name + Customers.CustomField1 AS Customer_Bill_Name,
               JrnlHdr.Reference, JrnlHdr.TransactionDate, JrnlHdr.DateDue, JrnlHdr.MainAmount, JrnlHdr.AmountPaid,
               JrnlHdr.ShipToName, JrnlHdr.ShipToAddress2, JrnlHdr.ShipToCity, JrnlHdr.ShipToZIP
        FROM JrnlHdr, Customers
        WHERE JrnlHdr.CustVendId = Customers.CustomerRecordNumber
          AND (JrnlHdr.MainAmount <> JrnlHdr.AmountPaid)
          AND (JrnlHdr.JrnlKey_Journal = 3)
          AND (JrnlHdr.JournalEx = 8)
          AND (JrnlHdr.JrnlTypeEx = 0)
          AND (NOT (JrnlHdr.Description LIKE 'ANULAD%'))
        ORDER BY JrnlHdr.PostOrder
        """;

    // Subtotal e IVA por factura: RowType 0 = subtotal, el resto IVA.
    private const string SqlGruposFmt = """
        SELECT PostOrder, RowType, TaxAuthorityCode, SUM(Amount) AS Subt
        FROM JrnlRow
        WHERE (RowNumber <> 0) AND (PostOrder IN ({0}))
        GROUP BY PostOrder, RowType, TaxAuthorityCode
        ORDER BY PostOrder, RowType, TaxAuthorityCode
        """;

    // Retenciones: notas de crédito (diario 3, tipo 9, NC = 2) con PurchOrder '4R' enlazadas a la factura.
    private const string SqlRetencionesFmt = """
        SELECT JrnlRow.LinkToAnotherTrx, JrnlRow.Amount, JrnlRow.RowDescription, Jobs.JobID
        FROM JrnlHdr CreditMemo, JrnlRow, Jobs
        WHERE CreditMemo.PostOrder = JrnlRow.PostOrder
          AND JrnlRow.JobRecordNumber = Jobs.JobRecordNumber
          AND (CreditMemo.JrnlKey_Journal = 3)
          AND (CreditMemo.JournalEx = 9)
          AND (CreditMemo.JrnlTypeEx = 2)
          AND (JrnlRow.RowType = 0)
          AND (CreditMemo.PurchOrder LIKE '4R')
          AND (JrnlRow.LinkToAnotherTrx IN ({0}))
        ORDER BY CreditMemo.PostOrder, JrnlRow.RowNumber
        """;

    public async Task<OpcionesPwc> OpcionesAsync(CancellationToken cancellationToken = default)
    {
        await using var cn = await sage.AbrirSesionAsync(cancellationToken);
        return ArmadorPwc.Opciones(await LeerCabecerasAsync(cn, cancellationToken));
    }

    public async Task<ResultadoPwc> GenerarAsync(FiltroPwc filtro, CancellationToken cancellationToken = default)
    {
        await using var cn = await sage.AbrirSesionAsync(cancellationToken);
        return await EjecutarAsync(cn, filtro, cancellationToken);
    }

    public async Task<ResultadoPwc> GenerarParaRucAsync(string ruc, FiltroPwc filtro, CancellationToken cancellationToken = default)
    {
        await using var cn = await sage.AbrirParaRucAsync(ruc, cancellationToken);
        return await EjecutarAsync(cn, filtro, cancellationToken);
    }

    private static async Task<ResultadoPwc> EjecutarAsync(OdbcConnection cn, FiltroPwc filtro, CancellationToken ct)
    {
        var elegidas = (await LeerCabecerasAsync(cn, ct)).Where(filtro.Coincide).ToList();
        if (elegidas.Count == 0)
        {
            return ResultadoPwc.Vacio;
        }

        var postOrders = elegidas.Select(c => c.PostOrder).ToList();
        var grupos = new List<GrupoImpuestoCrudo>();
        var retenciones = new List<RetencionCruda>();
        foreach (var lote in SageAcceso.Lotes(postOrders))
        {
            grupos.AddRange(await LeerGruposAsync(cn, lote, ct));
            retenciones.AddRange(await LeerRetencionesAsync(cn, lote, ct));
        }

        return new ResultadoPwc(ArmadorPwc.Armar(elegidas, grupos, retenciones));
    }

    private static async Task<IReadOnlyList<CabeceraFacturaPwc>> LeerCabecerasAsync(OdbcConnection cn, CancellationToken ct)
    {
        var lista = new List<CabeceraFacturaPwc>();
        await using var cmd = new OdbcCommand(SqlCabecera, cn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var emision = SageAcceso.Fecha(r, 3);
            if (emision is null)
            {
                continue;
            }
            lista.Add(new CabeceraFacturaPwc(
                PostOrder: Convert.ToInt64(r.GetValue(0)),
                Cliente: SageAcceso.Texto(r, 1),
                Factura: SageAcceso.Texto(r, 2),
                Emision: DateOnly.FromDateTime(emision.Value),
                Vence: SageAcceso.Fecha(r, 4) is { } v ? DateOnly.FromDateTime(v) : null,
                Total: SageAcceso.Decimal(r, 5),
                Pagado: SageAcceso.Decimal(r, 6),
                Anunciante: SageAcceso.Texto(r, 7),
                Direccion: SageAcceso.Texto(r, 8),
                Orden: SageAcceso.Texto(r, 9),
                Ciudad: SageAcceso.Texto(r, 10)));
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

    private static async Task<List<RetencionCruda>> LeerRetencionesAsync(
        OdbcConnection cn, IReadOnlyList<long> postOrders, CancellationToken ct)
    {
        await using var cmd = new OdbcCommand(string.Format(SqlRetencionesFmt, SageAcceso.Marcadores(postOrders.Count)), cn);
        SageAcceso.AgregarEnteros(cmd, postOrders);

        var lista = new List<RetencionCruda>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            lista.Add(new RetencionCruda(
                Convert.ToInt64(r.GetValue(0)),
                SageAcceso.Texto(r, 3),
                SageAcceso.Decimal(r, 1) ?? 0m,
                SageAcceso.Texto(r, 2)));
        }
        return lista;
    }
}
