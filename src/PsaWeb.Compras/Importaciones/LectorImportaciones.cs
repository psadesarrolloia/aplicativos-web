using System.Data.Common;
using System.Data.Odbc;
using System.Globalization;

namespace PsaWeb.Compras.Importaciones;

/// <summary>Ítem de stock de Sage (combo de la liquidación: <c>sageItems</c> con <c>StockItem</c>).</summary>
public sealed record ItemStock(string Id, string Descripcion, bool Inactivo);

/// <summary>Proveedor de Sage (ID y nombre) para el selector.</summary>
public sealed record ProveedorLiquidacion(string Id, string Nombre, bool Inactivo);

/// <summary>OC de una liquidación en Sage y su compra, si ya la tiene.</summary>
public sealed record OcLiquidacion(int PostOrder, string Referencia, DateTime Fecha, string ProveedorId, bool Cerrada, int? PostOrderCompra, string? ReferenciaCompra);

/// <summary>Lecturas por ODBC (SOLO SELECT) de la liquidación de importaciones, con las consultas del `.exe`.</summary>
public static class LectorImportaciones
{
    private const string FiltroCuentas = "AccountDescription LIKE 'IMPORTACION%'";

    /// <summary>
    /// Cuentas <c>IMPORTACION%</c> (<c>SageAccounts</c> del `.exe`, que además filtraba por texto) con el resumen de sus movimientos sin
    /// las OC: cantidad de filas, saldo (Σ montos redondeados a 2, como <c>sageImportMgm</c>) y fechas.
    /// </summary>
    public static async Task<IReadOnlyList<CuentaImportacion>> CuentasAsync(OdbcConnection cn, CancellationToken ct = default)
    {
        var cuentas = new List<(string Id, string Desc, bool Inactiva)>();
        await using (var cmd = new OdbcCommand($"SELECT AccountID, AccountDescription, AccountIsInactive FROM Chart WHERE {FiltroCuentas} ORDER BY AccountID", cn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct)) cuentas.Add((Texto(r, 0), Texto(r, 1), Entero(r, 2) != 0));
        }

        var resumen = new Dictionary<string, (int N, double Saldo, DateTime Desde, DateTime Hasta)>();
        await using (var cmd = new OdbcCommand(
            "SELECT c.AccountID, h.TransactionDate, r.Amount FROM JrnlHdr h, JrnlRow r, Chart c " +
            "WHERE h.PostOrder = r.PostOrder AND r.GLAcntNumber = c.GLAcntNumber AND c." + FiltroCuentas + " " +
            "AND h.JournalEx <> 18 AND h.JrnlKey_Journal <> 10", cn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                var id = Texto(r, 0);
                var fecha = Convert.ToDateTime(r.GetValue(1), CultureInfo.InvariantCulture);
                var monto = Math.Round(Doble(r, 2), 2);
                resumen[id] = resumen.TryGetValue(id, out var a)
                    ? (a.N + 1, a.Saldo + monto, fecha < a.Desde ? fecha : a.Desde, fecha > a.Hasta ? fecha : a.Hasta)
                    : (1, monto, fecha, fecha);
            }
        }

        return cuentas.Select(c => resumen.TryGetValue(c.Id, out var s)
                ? new CuentaImportacion(c.Id, c.Desc, c.Inactiva, s.N, s.Saldo, s.Desde, s.Hasta)
                : new CuentaImportacion(c.Id, c.Desc, c.Inactiva, 0, 0, null, null))
            .ToList();
    }

    public static async Task<CuentaImportacion?> CuentaAsync(OdbcConnection cn, string cuenta, CancellationToken ct = default)
    {
        await using var cmd = new OdbcCommand($"SELECT AccountID, AccountDescription, AccountIsInactive FROM Chart WHERE {FiltroCuentas} AND AccountID = ?", cn);
        Parametro(cmd, cuenta);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var gastos = await GastosAsync(cn, cuenta, ct);
        return new CuentaImportacion(Texto(r, 0), Texto(r, 1), Entero(r, 2) != 0, gastos.Count, gastos.Sum(x => x.Valor),
            gastos.Count == 0 ? null : gastos.Min(x => x.Fecha), gastos.Count == 0 ? null : gastos.Max(x => x.Fecha));
    }

    /// <summary>
    /// Filas de la cuenta (constructor de <c>sageImportMgm</c>): todo lo que no es OC, ordenado por fecha; monto redondeado a 2 y todas
    /// como gasto (la factura la marca el usuario).
    /// </summary>
    public static async Task<List<GastoImportacion>> GastosAsync(OdbcConnection cn, string cuenta, CancellationToken ct = default)
    {
        var lista = new List<GastoImportacion>();
        await using var cmd = new OdbcCommand(
            "SELECT h.TransactionDate, h.Reference, h.Description, r.RowDescription, r.Amount FROM JrnlHdr h, JrnlRow r, Chart c " +
            "WHERE h.PostOrder = r.PostOrder AND r.GLAcntNumber = c.GLAcntNumber AND c.AccountID = ? " +
            "AND h.JournalEx <> 18 AND h.JrnlKey_Journal <> 10 ORDER BY h.TransactionDate, h.PostOrder, r.RowNumber", cn);
        Parametro(cmd, cuenta);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            lista.Add(new GastoImportacion
            {
                Fecha = Convert.ToDateTime(r.GetValue(0), CultureInfo.InvariantCulture),
                Referencia = Texto(r, 1),
                Proveedor = Texto(r, 2),
                Descripcion = Texto(r, 3),
                Valor = Math.Round(Doble(r, 4), 2),
                EsGasto = true,
            });
        }
        return lista;
    }

    /// <summary>OC por PostOrder (si sigue en Sage) y la compra que la recibe.</summary>
    public static async Task<OcLiquidacion?> OcAsync(OdbcConnection cn, int postOrder, CancellationToken ct = default)
    {
        await using var cmd = new OdbcCommand(
            "SELECT h.PostOrder, h.Reference, h.TransactionDate, v.VendorID, h.POSOisClosed, h.CustVendId FROM JrnlHdr h, Vendors v " +
            "WHERE h.CustVendId = v.VendorRecordNumber AND h.JrnlKey_Journal = 10 AND h.JournalEx = 18 AND h.PostOrder = ?", cn);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = postOrder });
        return await LeerOcAsync(cn, cmd, ct);
    }

    /// <summary>OC por referencia y proveedor (recupera el vínculo si la liquidación guardada no alcanzó a anotar el PostOrder).</summary>
    public static async Task<OcLiquidacion?> OcPorReferenciaAsync(OdbcConnection cn, string referencia, string proveedorId, CancellationToken ct = default)
    {
        await using var cmd = new OdbcCommand(
            "SELECT h.PostOrder, h.Reference, h.TransactionDate, v.VendorID, h.POSOisClosed, h.CustVendId FROM JrnlHdr h, Vendors v " +
            "WHERE h.CustVendId = v.VendorRecordNumber AND h.JrnlKey_Journal = 10 AND h.JournalEx = 18 AND h.Reference = ? AND v.VendorID = ? " +
            "ORDER BY h.PostOrder DESC", cn);
        Parametro(cmd, referencia);
        Parametro(cmd, proveedorId);
        return await LeerOcAsync(cn, cmd, ct);
    }

    /// <summary>De esos PostOrder, los que siguen siendo OC en Sage (estado de la lista).</summary>
    public static async Task<HashSet<int>> OcsExistentesAsync(OdbcConnection cn, IReadOnlyCollection<int> postOrders, CancellationToken ct = default)
    {
        var r = new HashSet<int>();
        foreach (var lote in postOrders.Chunk(200))
        {
            await using var cmd = new OdbcCommand($"SELECT PostOrder FROM JrnlHdr WHERE JrnlKey_Journal = 10 AND JournalEx = 18 AND PostOrder IN ({string.Join(",", lote)})", cn);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) r.Add(Entero(rd, 0));
        }
        return r;
    }

    /// <summary>¿Hay otra OC con esa referencia? (el Bridge lo vuelve a validar al guardar).</summary>
    public static async Task<bool> ReferenciaUsadaAsync(OdbcConnection cn, string referencia, int? excepto, CancellationToken ct = default)
    {
        await using var cmd = new OdbcCommand("SELECT COUNT(*) FROM JrnlHdr WHERE JrnlKey_Journal = 10 AND JournalEx = 18 AND Reference = ? AND PostOrder <> ?", cn);
        Parametro(cmd, referencia);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = excepto ?? -1 });
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>
    /// Cuenta por pagar de la empresa (C6): la que más usan sus compras del último año entre las de tipo «por pagar» (CPTDC
    /// <c>20000</c>, SANCEV <c>20000-513</c>). Nula si no hay compras.
    /// </summary>
    public static Task<string?> CuentaPorPagarAsync(OdbcConnection cn, CancellationToken ct = default) =>
        PsaWeb.Compras.Catalogo.LectorCatalogoCompras.CuentaPorPagarAsync(cn, ct);

    /// <summary>
    /// Convención de la referencia de la compra de una liquidación en la empresa, según sus liquidaciones de los últimos dos años: true =
    /// la de la OC con el primer guion cambiado por espacio (CPTDC: <c>LIQ IMPORT 041-2026</c>); false = igual a la de la OC (SANCEV).
    /// Sin historial: con espacio.
    /// </summary>
    public static async Task<bool> CompraConEspacioAsync(OdbcConnection cn, CancellationToken ct = default)
    {
        int espacio = 0, igual = 0;
        await using var cmd = new OdbcCommand(
            "SELECT p.Reference, h.Reference FROM JrnlHdr p, JrnlHdr h, JrnlRow r WHERE p.JrnlKey_Journal = 10 AND p.JournalEx = 18 " +
            "AND h.JrnlKey_Journal = 4 AND h.INV_POSOOrderNumber = p.Reference AND h.CustVendId = p.CustVendId AND r.PostOrder = p.PostOrder " +
            "AND r.ItemRecordNumber = 0 AND r.RowDescription = 'LIQUIDACION' AND p.TransactionDate >= ?", cn);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = DateTime.Today.AddYears(-2) });
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var oc = Texto(rd, 0);
            var compra = Texto(rd, 1);
            if (compra == oc) igual++;
            else if (compra == PsaWeb.SageBridge.Contratos.ReferenciasLiquidacion.DeCompra(oc)) espacio++;
        }
        return espacio >= igual;
    }

    /// <summary>Ítems de stock (<c>ItemClass</c> 1), activos e inactivos, como el combo del `.exe`.</summary>
    public static async Task<IReadOnlyList<ItemStock>> ItemsStockAsync(OdbcConnection cn, CancellationToken ct = default)
    {
        var lista = new List<ItemStock>();
        await using var cmd = new OdbcCommand("SELECT ItemID, ItemDescription, ItemIsInactive FROM LineItem WHERE ItemClass = 1 ORDER BY ItemID", cn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lista.Add(new ItemStock(Texto(r, 0), Texto(r, 1), Entero(r, 2) != 0));
        return lista;
    }

    public static async Task<IReadOnlyList<ProveedorLiquidacion>> ProveedoresAsync(OdbcConnection cn, CancellationToken ct = default)
    {
        var lista = new List<ProveedorLiquidacion>();
        await using var cmd = new OdbcCommand("SELECT VendorID, Name, IsInactive FROM Vendors ORDER BY VendorID", cn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lista.Add(new ProveedorLiquidacion(Texto(r, 0), Texto(r, 1), Entero(r, 2) != 0));
        return lista;
    }

    private static async Task<OcLiquidacion?> LeerOcAsync(OdbcConnection cn, OdbcCommand cmd, CancellationToken ct)
    {
        int po, vendor;
        string referencia, proveedor;
        DateTime fecha;
        bool cerrada;
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return null;
            po = Entero(r, 0);
            referencia = Texto(r, 1);
            fecha = Convert.ToDateTime(r.GetValue(2), CultureInfo.InvariantCulture);
            proveedor = Texto(r, 3);
            cerrada = Entero(r, 4) != 0;
            vendor = Entero(r, 5);
        }
        await using var c = new OdbcCommand(
            // La compra manual (y la del Bridge antes de C7) queda aplicada a la OC; la del Bridge, con la referencia de compra (C7).
            "SELECT PostOrder, Reference FROM JrnlHdr WHERE JrnlKey_Journal = 4 AND JournalEx = 11 AND (INV_POSOOrderNumber = ? OR Reference = ? OR Reference = ?) " +
            "AND CustVendId = ? ORDER BY PostOrder DESC", cn);
        Parametro(c, referencia);
        Parametro(c, PsaWeb.SageBridge.Contratos.ReferenciasLiquidacion.DeCompra(referencia));
        Parametro(c, referencia);
        c.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = vendor });
        await using var rc = await c.ExecuteReaderAsync(ct);
        return await rc.ReadAsync(ct)
            ? new OcLiquidacion(po, referencia, fecha, proveedor, cerrada, Entero(rc, 0), Texto(rc, 1))
            : new OcLiquidacion(po, referencia, fecha, proveedor, cerrada, null, null);
    }

    private static void Parametro(OdbcCommand cmd, string valor) =>
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = valor });

    private static string Texto(DbDataReader r, int i) =>
        r.IsDBNull(i) ? string.Empty : (Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty).Trim();

    private static int Entero(DbDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);

    private static double Doble(DbDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToDouble(r.GetValue(i), CultureInfo.InvariantCulture);
}
