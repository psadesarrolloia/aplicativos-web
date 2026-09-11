using System.Data;
using System.Data.Odbc;
using Microsoft.Extensions.Logging;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.Kardex.Data;

/// <summary>
/// Implementación real contra la base de Sage 50 (Pervasive / Actian Zen vía
/// ODBC). Reproduce la pantalla «Reporte de Stock» de <c>Sage50usIntegration</c>:
/// <list type="bullet">
///   <item>lista de ítems stock (<c>LineItem</c>, <c>ItemClass = 1</c>);</item>
///   <item>cuentas de inventario (las cuentas de <c>Chart</c> referidas por esos ítems);</item>
///   <item>kardex por ítem: las 3 consultas de <c>InventroyCostQuery</c>, con la
///   tercera (saldo por movimiento) <b>colapsada</b> de N+1 a una sola.</item>
/// </list>
/// La empresa la resuelve <see cref="IResolverEmpresaSage"/>: con shell, por el
/// RUC de sesión; sin shell, la cadena de <c>Sage50:ConnectionString</c>.
/// Todas las consultas usan <see cref="OdbcParameter"/> posicionales.
/// </summary>
internal sealed class OdbcKardexRepository : IKardexRepository
{
    // Ítems stock: ItemClass = 1 (verificado en CPTDC; 0 son pseudo-ítems de
    // impuesto/retención). Mismo criterio que sageItems del .exe: (int)StockItem - 1.
    private const string SqlItems = """
        SELECT ItemID, ItemDescription, Category, InvAcctRecordNumber
        FROM LineItem
        WHERE (ItemIsInactive = 0) AND ItemClass = 1
        ORDER BY ItemID
        """;

    private const string SqlChart = """
        SELECT GLAcntNumber, AccountID, AccountDescription, AccountType FROM Chart
        """;

    // AccountType de Sage 50 para cuentas de inventario (verificado en CPTDC:
    // las 13xxx "INVENTARIOS..." son todas AccountType 2; es un enum fijo del
    // plan de cuentas de Sage, no un valor libre por empresa).
    private const long TipoCuentaInventario = 2;

    // Q1 — saldo inicial: por ítem, la última fila MajorType 3 previa a «Desde».
    private const string SqlInicialesFmt = """
        SELECT LineItem.ItemID, InventoryCosts.TransDate, JrnlHdr.Reference,
               InventoryCosts.PostOrderNumber, InventoryCosts.TransAmount,
               InventoryCosts.Quantity, InventoryCosts.OptAmount, InventoryCosts.MajorType
        FROM LineItem, InventoryCosts, JrnlHdr
        WHERE LineItem.ItemRecordNumber = InventoryCosts.ItemRecNumber
          AND InventoryCosts.PostOrderNumber = JrnlHdr.PostOrder
          AND InventoryCosts.TransDate < ?
          AND InventoryCosts.MajorType = 3
          AND LineItem.ItemID IN ({0})
        ORDER BY LineItem.ItemID, InventoryCosts.TransDate, InventoryCosts.PostOrderNumber
        """;

    // Q2 — movimientos del rango (compras y ventas).
    private const string SqlMovimientosFmt = """
        SELECT LineItem.ItemID, InventoryCosts.TransDate, JrnlHdr.Reference,
               InventoryCosts.PostOrderNumber, InventoryCosts.TransAmount,
               InventoryCosts.Quantity, InventoryCosts.OptAmount, InventoryCosts.MajorType
        FROM LineItem, InventoryCosts, JrnlHdr
        WHERE LineItem.ItemRecordNumber = InventoryCosts.ItemRecNumber
          AND InventoryCosts.PostOrderNumber = JrnlHdr.PostOrder
          AND (InventoryCosts.TransDate BETWEEN ? AND ?)
          AND InventoryCosts.MajorType <> 3
          AND LineItem.ItemID IN ({0})
        ORDER BY LineItem.ItemID, InventoryCosts.TransDate, InventoryCosts.MajorType, InventoryCosts.PostOrderNumber
        """;

    // Q3 — saldo por movimiento, colapsada: todos los MajorType 3 cuyos PostOrder
    // salieron de Q2. (El .exe hacía una consulta por cada fila de movimiento.)
    private const string SqlSaldosFmt = """
        SELECT LineItem.ItemID, InventoryCosts.TransDate, JrnlHdr.Reference,
               InventoryCosts.PostOrderNumber, InventoryCosts.TransAmount,
               InventoryCosts.Quantity, InventoryCosts.OptAmount, InventoryCosts.MajorType
        FROM LineItem, InventoryCosts, JrnlHdr
        WHERE LineItem.ItemRecordNumber = InventoryCosts.ItemRecNumber
          AND InventoryCosts.PostOrderNumber = JrnlHdr.PostOrder
          AND InventoryCosts.MajorType = 3
          AND LineItem.ItemID IN ({0})
          AND InventoryCosts.PostOrderNumber IN ({1})
        ORDER BY LineItem.ItemID, InventoryCosts.TransDate, InventoryCosts.MajorType, InventoryCosts.PostOrderNumber
        """;

    private const int LoteIn = 200;

    private readonly ISageConnectionFactory _connections;
    private readonly IResolverEmpresaSage _resolver;
    private readonly ILogger<OdbcKardexRepository> _logger;

    public OdbcKardexRepository(
        ISageConnectionFactory connections,
        IResolverEmpresaSage resolver,
        ILogger<OdbcKardexRepository> logger)
    {
        _connections = connections;
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ItemStock>> ItemsAsync(CancellationToken cancellationToken = default)
    {
        await using var cn = await AbrirAsync(await CadenaSesionAsync(cancellationToken), cancellationToken);
        return (await LeerCatalogoAsync(cn, cancellationToken)).Items;
    }

    public async Task<IReadOnlyList<CuentaInventario>> CuentasAsync(CancellationToken cancellationToken = default)
    {
        await using var cn = await AbrirAsync(await CadenaSesionAsync(cancellationToken), cancellationToken);
        return (await LeerCatalogoAsync(cn, cancellationToken)).Cuentas;
    }

    public async Task<ResultadoKardex> GenerarAsync(FiltroKardex filtro, CancellationToken cancellationToken = default)
        => await EjecutarAsync(await CadenaSesionAsync(cancellationToken), filtro, cancellationToken);

    public async Task<ResultadoKardex> GenerarParaRucAsync(
        string ruc, FiltroKardex filtro, CancellationToken cancellationToken = default)
        => await EjecutarAsync(await _resolver.CadenaOdbcAsync(ruc, cancellationToken), filtro, cancellationToken);

    // --- interno -------------------------------------------------------------

    private async Task<string?> CadenaSesionAsync(CancellationToken ct)
        => _resolver.RucSesion is { } ruc ? await _resolver.CadenaOdbcAsync(ruc, ct) : null;

    private async Task<OdbcConnection> AbrirAsync(string? cadena, CancellationToken ct)
    {
        var cn = cadena is null ? _connections.CreateConnection() : _connections.CreateConnection(cadena);
        await cn.OpenAsync(ct);
        return cn;
    }

    private async Task<ResultadoKardex> EjecutarAsync(string? cadena, FiltroKardex filtro, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(cadena, ct);

        var (items, _) = await LeerCatalogoAsync(cn, ct);
        var elegidos = SeleccionItems.Filtrar(items, filtro);
        if (elegidos.Count == 0)
        {
            return ResultadoKardex.Vacio;
        }

        var ids = elegidos.Select(i => i.Id).ToList();
        var desde = filtro.Desde.ToDateTime(TimeOnly.MinValue);
        var hasta = filtro.Hasta.ToDateTime(TimeOnly.MinValue);

        var iniciales = new List<CostoCrudo>();
        var movimientos = new List<CostoCrudo>();
        foreach (var lote in Lotes(ids, LoteIn))
        {
            iniciales.AddRange(await LeerAsync(cn, string.Format(SqlInicialesFmt, Marcadores(lote.Count)),
                cmd => { AgregarFecha(cmd, desde); AgregarIds(cmd, lote); }, ct));
            movimientos.AddRange(await LeerAsync(cn, string.Format(SqlMovimientosFmt, Marcadores(lote.Count)),
                cmd => { AgregarFecha(cmd, desde); AgregarFecha(cmd, hasta); AgregarIds(cmd, lote); }, ct));
        }

        var postOrders = movimientos.Where(m => m.MajorType != 3).Select(m => m.PostOrder).Distinct().ToList();
        var saldos = new List<CostoCrudo>();
        foreach (var loteIds in Lotes(ids, LoteIn))
        {
            foreach (var lotePo in Lotes(postOrders, LoteIn))
            {
                saldos.AddRange(await LeerAsync(cn,
                    string.Format(SqlSaldosFmt, Marcadores(loteIds.Count), Marcadores(lotePo.Count)),
                    cmd => { AgregarIds(cmd, loteIds); AgregarPostOrders(cmd, lotePo); }, ct));
            }
        }

        var filas = ArmadorKardex.Armar(
            elegidos, iniciales, movimientos, saldos, filtro.Desde, filtro.IncluirVacios);
        return new ResultadoKardex(filas);
    }

    private async Task<(IReadOnlyList<ItemStock> Items, IReadOnlyList<CuentaInventario> Cuentas)>
        LeerCatalogoAsync(OdbcConnection cn, CancellationToken ct)
    {
        // GLAcntNumber -> (AccountID, AccountDescription, AccountType)
        var chart = new Dictionary<long, (string Id, string Desc, long Tipo)>();
        await using (var cmd = new OdbcCommand(SqlChart, cn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                if (r.IsDBNull(0)) continue;
                var id = r.IsDBNull(1) ? "" : (r.GetValue(1)?.ToString() ?? "").Trim();
                var desc = r.IsDBNull(2) ? "" : (r.GetValue(2)?.ToString() ?? "").Trim();
                var tipo = r.IsDBNull(3) ? -1L : Convert.ToInt64(r.GetValue(3));
                chart[Convert.ToInt64(r.GetValue(0))] = (id, desc, tipo);
            }
        }

        var items = new List<ItemStock>();
        await using (var cmd = new OdbcCommand(SqlItems, cn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                var id = (r.GetValue(0)?.ToString() ?? "").Trim();
                if (id.Length == 0) continue;
                var nombre = r.IsDBNull(1) ? "" : (r.GetValue(1)?.ToString() ?? "").Trim();
                var categoria = r.IsDBNull(2) ? "" : (r.GetValue(2)?.ToString() ?? "").Trim();
                var invAcct = r.IsDBNull(3) ? "" : (r.GetValue(3)?.ToString() ?? "").Trim();
                var cuenta = long.TryParse(invAcct, out var acct) && chart.TryGetValue(acct, out var c)
                    ? c.Id
                    : "";
                items.Add(new ItemStock(id, nombre, categoria, cuenta));
            }
        }

        // Sólo cuentas de tipo Inventario (AccountType 2): CPTDC tiene ítems mal
        // clasificados (ItemClass=1 pero en realidad servicios, ej. S-001..S-008,
        // S-045..S-058) cuya Cuenta de Inventario en Sage apunta, por error de
        // configuración, a una cuenta que no es de inventario (ej. 18000, una
        // cuenta por cobrar) — sin este filtro esas cuentas «contaminaban» el
        // selector. Es un problema de datos de Sage, no algo que se oculte: esos
        // ítems se siguen pudiendo elegir por ID o por rango, sólo no aparece su
        // cuenta como opción de filtro.
        var infoPorId = chart.Values.Where(c => c.Id.Length > 0)
            .GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());

        var cuentas = items.Where(i => !string.IsNullOrEmpty(i.CuentaGl))
            .Select(i => i.CuentaGl)
            .Distinct()
            .Where(c => infoPorId.TryGetValue(c, out var info) && info.Tipo == TipoCuentaInventario)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Select(c => new CuentaInventario(c, infoPorId[c].Desc))
            .ToList();

        return (items, cuentas);
    }

    private static async Task<List<CostoCrudo>> LeerAsync(
        OdbcConnection cn, string sql, Action<OdbcCommand> parametros, CancellationToken ct)
    {
        await using var cmd = new OdbcCommand(sql, cn);
        parametros(cmd);

        var filas = new List<CostoCrudo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            filas.Add(new CostoCrudo(
                ItemId: (r.GetValue(0)?.ToString() ?? "").Trim(),
                TransDate: r.IsDBNull(1) ? default : Convert.ToDateTime(r.GetValue(1)),
                Reference: r.IsDBNull(2) ? null : (r.GetValue(2)?.ToString() ?? "").Trim(),
                PostOrder: Convert.ToInt64(r.GetValue(3)),
                TransAmount: r.IsDBNull(4) ? null : Convert.ToDecimal(r.GetValue(4)),
                Quantity: r.IsDBNull(5) ? null : Convert.ToDecimal(r.GetValue(5)),
                OptAmount: r.IsDBNull(6) ? null : Convert.ToDecimal(r.GetValue(6)),
                MajorType: Convert.ToInt64(r.GetValue(7))));
        }
        return filas;
    }

    private static string Marcadores(int n) => string.Join(", ", Enumerable.Repeat("?", n));

    private static void AgregarFecha(OdbcCommand cmd, DateTime valor)
        => cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = valor });

    private static void AgregarIds(OdbcCommand cmd, IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = id });
        }
    }

    private static void AgregarPostOrders(OdbcCommand cmd, IEnumerable<long> postOrders)
    {
        // El .exe lee PostOrderNumber como Int32; la columna Pervasive es entera.
        foreach (var po in postOrders)
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = (int)po });
        }
    }

    private static IEnumerable<IReadOnlyList<T>> Lotes<T>(IReadOnlyList<T> origen, int tamano)
    {
        for (var i = 0; i < origen.Count; i += tamano)
        {
            yield return origen.Skip(i).Take(tamano).ToList();
        }
    }
}
