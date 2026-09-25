using System.Data.Common;
using System.Data.Odbc;
using System.Globalization;
using PsaWeb.Compras.Armado;
using PsaWeb.SageBridge.Contratos;

namespace PsaWeb.Compras.Sage;

/// <summary>Estado de una OC en la lista (<c>batchXmlBills.CompStatus</c>).</summary>
public enum EstadoOc
{
    /// <summary>Solo la OC: se puede actualizar.</summary>
    Guardado,
    /// <summary>Ya existe la compra que la recibe (<c>INV_POSOOrderNumber</c> = nº de OC): solo lectura.</summary>
    Contabilizado,
}

/// <summary>Fila de la lista de compras (<c>FrmPrintedPurchases</c>).</summary>
public sealed record OcResumen(
    int PostOrder, string Referencia, EstadoOc Estado, DateTime Fecha, string Proveedor, string Identificacion,
    string Factura, string Retencion, string Autorizacion)
{
    /// <summary>Clave de acceso de 49 dígitos: comprobante electrónico (no se permite «Copiar»).</summary>
    public bool EsElectronica => Autorizacion.Length > 40;
}

/// <summary>Cabecera de una OC guardada.</summary>
public sealed record OcGuardadaCabecera(
    int PostOrder, string Referencia, DateTime Fecha, DateTime? FechaRegistro, string ShipVia, string Factura,
    string Retencion, string Estado, string Zip, string VendorId, bool Recibida);

/// <summary>Fila de una OC guardada (<c>JrnlRow</c> con su ítem, categoría, cuenta y job).</summary>
public sealed record OcGuardadaFila(
    int Numero, string ItemId, string Categoria, string Descripcion, decimal Cantidad, decimal PrecioUnitario,
    decimal Monto, string Cuenta, string Job);

public sealed record OcGuardada(OcGuardadaCabecera Cabecera, IReadOnlyList<OcGuardadaFila> Filas)
{
    /// <summary>Descripción de la fila <c>AUT-SRI</c>: clave de acceso o autorización.</summary>
    public string Autorizacion => Filas.FirstOrDefault(f => f.ItemId == "AUT-SRI")?.Descripcion ?? string.Empty;
}

public sealed record CuentaSage(string Id, string Descripcion);
public sealed record JobSage(string Id, string Descripcion);

/// <summary>Lecturas por ODBC (SOLO SELECT) del módulo Compras: lista, OC guardada, cuentas, jobs y vista previa de números.</summary>
public static class LectorOcs
{
    private const string FiltroOc = "JrnlKey_Journal = 10 AND JournalEx = 18";

    /// <summary>OC por rango de fechas y tipo, con estado y autorización (<c>FrmPrintedPurchases.updateQuery</c>).</summary>
    public static async Task<IReadOnlyList<OcResumen>> ListarAsync(
        OdbcConnection cn, DateTime desde, DateTime hasta, TipoDocumentoCompra tipo, CancellationToken ct = default)
    {
        var cabeceras = new List<(int Po, DateTime Fecha, string Ref, string Terminos, int Vendor, string Ret)>();
        await using (var cmd = new OdbcCommand(
            $"SELECT PostOrder, TransactionDate, Reference, TermsDescription, CustVendId, ShipToAddress2 FROM JrnlHdr " +
            $"WHERE {FiltroOc} AND TransactionDate BETWEEN ? AND ? AND ShipVia = ? ORDER BY TransactionDate, Reference", cn))
        {
            Fecha(cmd, desde);
            Fecha(cmd, hasta);
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = TiposDocumentoCompra.Descripcion(tipo) });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                cabeceras.Add((Convert.ToInt32(r.GetValue(0)), Convert.ToDateTime(r.GetValue(1)), Texto(r, 2), Texto(r, 3),
                    Convert.ToInt32(r.GetValue(4)), Texto(r, 5)));
            }
        }
        if (cabeceras.Count == 0) return [];

        var proveedores = new Dictionary<int, (string Nombre, string Id)>();
        await using (var cmd = new OdbcCommand(
            "SELECT v.VendorRecordNumber, v.Name, v.CustomField0, a.Country FROM Vendors v, Address a " +
            "WHERE v.VendorRecordNumber = a.VendorRecordNumber", cn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            var usados = cabeceras.Select(c => c.Vendor).ToHashSet();
            while (await r.ReadAsync(ct))
            {
                var rec = Convert.ToInt32(r.GetValue(0));
                if (usados.Contains(rec)) proveedores[rec] = (Texto(r, 1) + Texto(r, 2), Texto(r, 3));
            }
        }

        // Compras que reciben estas OC (mismo proveedor y nº de OC).
        var recibidas = new HashSet<(int, string)>();
        await using (var cmd = new OdbcCommand(
            "SELECT CustVendId, INV_POSOOrderNumber FROM JrnlHdr WHERE JrnlKey_Journal = 4 AND JournalEx = 11 AND TransactionDate >= ?", cn))
        {
            Fecha(cmd, desde.AddDays(-1));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) recibidas.Add((Convert.ToInt32(r.GetValue(0)), Texto(r, 1)));
        }

        var claves = new Dictionary<int, string>();
        var postOrders = cabeceras.Select(c => c.Po).ToList();
        for (var i = 0; i < postOrders.Count; i += 200)
        {
            var lote = postOrders.Skip(i).Take(200).ToList();
            await using var cmd = new OdbcCommand(
                "SELECT r.PostOrder, r.RowDescription FROM JrnlRow r, LineItem l WHERE r.ItemRecordNumber = l.ItemRecordNumber " +
                $"AND l.ItemID = 'AUT-SRI' AND r.PostOrder IN ({string.Join(", ", lote.Select(_ => "?"))})", cn);
            foreach (var po in lote) cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = po });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) claves[Convert.ToInt32(r.GetValue(0))] = Texto(r, 1);
        }

        return cabeceras.Select(c =>
        {
            var prov = proveedores.GetValueOrDefault(c.Vendor);
            return new OcResumen(c.Po, c.Ref, recibidas.Contains((c.Vendor, c.Ref)) ? EstadoOc.Contabilizado : EstadoOc.Guardado,
                c.Fecha, prov.Nombre ?? string.Empty, prov.Id ?? string.Empty, c.Terminos, c.Ret, claves.GetValueOrDefault(c.Po) ?? string.Empty);
        }).ToList();
    }

    /// <summary>Una OC con todas sus filas (sin la fila 0 de cuentas por pagar).</summary>
    public static async Task<OcGuardada?> LeerAsync(OdbcConnection cn, int postOrder, CancellationToken ct = default)
    {
        OcGuardadaCabecera cab;
        await using (var cmd = new OdbcCommand(
            "SELECT h.PostOrder, h.Reference, h.TransactionDate, h.GoodThruDate, h.ShipVia, h.TermsDescription, h.ShipToAddress2, " +
            "h.ShipToState, h.ShipToZIP, v.VendorID FROM JrnlHdr h, Vendors v " +
            $"WHERE h.CustVendId = v.VendorRecordNumber AND h.JrnlKey_Journal = 10 AND h.JournalEx = 18 AND h.PostOrder = ?", cn))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = postOrder });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            cab = new OcGuardadaCabecera(Convert.ToInt32(r.GetValue(0)), Texto(r, 1), Convert.ToDateTime(r.GetValue(2)),
                r.IsDBNull(3) ? null : Convert.ToDateTime(r.GetValue(3)), Texto(r, 4), Texto(r, 5), Texto(r, 6), Texto(r, 7),
                Texto(r, 8), Texto(r, 9), false);
        }

        var filas = new List<OcGuardadaFila>();
        var recibida = false;
        await using (var cmd = new OdbcCommand(
            "SELECT r.RowNumber, l.ItemID, l.Category, r.RowDescription, r.Quantity, r.UnitCost, r.Amount, c.AccountID, j.JobID, r.StockingQtyReceived " +
            "FROM JrnlRow r LEFT OUTER JOIN LineItem l ON r.ItemRecordNumber = l.ItemRecordNumber " +
            "LEFT OUTER JOIN Chart c ON r.GLAcntNumber = c.GLAcntNumber LEFT OUTER JOIN Jobs j ON r.JobRecordNumber = j.JobRecordNumber " +
            "WHERE r.PostOrder = ? AND r.RowNumber > 0 ORDER BY r.RowNumber", cn))
        {
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = postOrder });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                filas.Add(new OcGuardadaFila(Convert.ToInt32(r.GetValue(0)), Texto(r, 1), Texto(r, 2), Texto(r, 3), Dec(r, 4), Dec(r, 5),
                    Dec(r, 6), Texto(r, 7), Texto(r, 8)));
                if (Dec(r, 9) != 0) recibida = true;
            }
        }
        return new OcGuardada(cab with { Recibida = recibida }, filas);
    }

    /// <summary>
    /// OC que llevan esas claves en su línea <c>AUT-SRI</c> (la bandeja de recibidos: «Guardado» / «Registrado»). La compra que
    /// genera el worker no copia la línea AUT-SRI (cantidad 0), así que se busca en las OC y se mira si ya se recibieron.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, (int PostOrder, string Referencia, bool Recibida)>> OcsPorAutorizacionAsync(
        OdbcConnection cn, IReadOnlyCollection<string> claves, CancellationToken ct = default)
    {
        var resultado = new Dictionary<string, (int, string, bool)>();
        foreach (var lote in claves.Distinct().Chunk(200))
        {
            var encontrados = new List<(string Clave, int Po, string Ref)>();
            await using (var cmd = new OdbcCommand(
                "SELECT r.RowDescription, h.PostOrder, h.Reference FROM JrnlRow r, LineItem l, JrnlHdr h " +
                "WHERE r.ItemRecordNumber = l.ItemRecordNumber AND r.PostOrder = h.PostOrder AND l.ItemID = 'AUT-SRI' " +
                $"AND h.JrnlKey_Journal = 10 AND h.JournalEx = 18 AND r.RowDescription IN ({string.Join(", ", lote.Select(_ => "?"))})", cn))
            {
                foreach (var c in lote) cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = c });
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) encontrados.Add((Texto(r, 0), Convert.ToInt32(r.GetValue(1)), Texto(r, 2)));
            }
            if (encontrados.Count == 0) continue;
            var recibidas = new HashSet<int>();
            await using (var cmd = new OdbcCommand(
                $"SELECT DISTINCT PostOrder FROM JrnlRow WHERE StockingQtyReceived <> 0 AND PostOrder IN ({string.Join(", ", encontrados.Select(_ => "?"))})", cn))
            {
                foreach (var e in encontrados) cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = e.Po });
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) recibidas.Add(Convert.ToInt32(r.GetValue(0)));
            }
            // Una clave en varias OC (p. ej. una duplicada a mano): manda la que ya se convirtió en compra.
            foreach (var e in encontrados)
            {
                var recibida = recibidas.Contains(e.Po);
                if (!resultado.TryGetValue(e.Clave, out var previa) || (recibida && !previa.Item3)) resultado[e.Clave] = (e.Po, e.Ref, recibida);
            }
        }
        return resultado;
    }

    /// <summary>Plan de cuentas (para los combos de cuenta).</summary>
    public static async Task<IReadOnlyList<CuentaSage>> CuentasAsync(OdbcConnection cn, CancellationToken ct = default)
    {
        var lista = new List<CuentaSage>();
        await using var cmd = new OdbcCommand("SELECT AccountID, AccountDescription FROM Chart WHERE AccountIsInactive = 0 ORDER BY AccountID", cn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lista.Add(new CuentaSage(Texto(r, 0), Texto(r, 1)));
        return lista;
    }

    public static async Task<IReadOnlyList<JobSage>> JobsAsync(OdbcConnection cn, CancellationToken ct = default)
    {
        var lista = new List<JobSage>();
        await using var cmd = new OdbcCommand("SELECT JobID, JobDescription FROM Jobs WHERE JobIsInactive = 0 ORDER BY JobID", cn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lista.Add(new JobSage(Texto(r, 0), Texto(r, 1)));
        return lista;
    }

    /// <summary>Prefijo de la última OC grabada (<c>LastPrefixInserted</c>): el de la OC de mayor PostOrder con <c>xx-</c>.</summary>
    public static async Task<string> UltimoPrefijoAsync(OdbcConnection cn, CancellationToken ct = default)
    {
        await using var cmd = new OdbcCommand($"SELECT TOP 1 Reference FROM JrnlHdr WHERE {FiltroOc} AND Reference LIKE '__-%' ORDER BY PostOrder DESC", cn);
        var r = Convert.ToString(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        return r.Length > 2 && r[2] == '-' ? r[..2] : "OC";
    }

    /// <summary>Vista previa del próximo nº de OC (el definitivo lo asigna el Bridge al guardar).</summary>
    public static async Task<string> ProximaOcAsync(OdbcConnection cn, string prefijo, CancellationToken ct = default)
        => NumeracionCompras.SiguienteOc(prefijo, await ListaAsync(cn, $"SELECT Reference FROM JrnlHdr WHERE {FiltroOc} AND Reference LIKE ?", prefijo + "-%", ct));

    /// <summary>Vista previa del próximo nº de retención de la serie.</summary>
    public static async Task<string> ProximaRetencionAsync(OdbcConnection cn, string serie, int secuencialInicial, CancellationToken ct = default)
        => NumeracionCompras.SiguienteRetencion(serie, await ListaAsync(cn, $"SELECT ShipToAddress2 FROM JrnlHdr WHERE {FiltroOc} AND ShipToAddress2 LIKE ?", serie + "-%", ct), secuencialInicial);

    private static async Task<List<string>> ListaAsync(OdbcConnection cn, string sql, string parametro, CancellationToken ct)
    {
        var lista = new List<string>();
        await using var cmd = new OdbcCommand(sql, cn);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = parametro });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lista.Add(Texto(r, 0));
        return lista;
    }

    private static void Fecha(OdbcCommand cmd, DateTime valor) =>
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = valor.Date });

    private static string Texto(DbDataReader r, int i) =>
        r.IsDBNull(i) ? string.Empty : (Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty).Trim();

    private static decimal Dec(DbDataReader r, int i) => r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i), CultureInfo.InvariantCulture);
}
