using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

namespace PsaWeb.SageBridge.Logica;

/// <summary>
/// Lecturas por ODBC (SOLO SELECT) de la empresa del trabajo: numeración y búsqueda de la OC existente, igual que
/// <c>sagePurchaseOrders</c> del `.exe`. La cadena se arma como la web (<c>PeachConnStringResolver</c>): fila de
/// <c>PeachEBills.PeachConnString</c> con la contraseña descifrada, y los overrides de desarrollo del Bridge.
/// </summary>
public sealed class OdbcSage : IDisposable
{
    private const string FiltroOc = "JrnlKey_Journal = 10 AND JournalEx = 18";
    private readonly OdbcConnection _cn;

    private OdbcSage(string cadena)
    {
        _cn = new OdbcConnection(cadena);
        _cn.Open();
    }

    public static OdbcSage Abrir(Configuracion cfg, string ruc)
    {
        string driver, dsn, uid, pwd;
        string? servidor, dbq;
        using (var cn = new SqlConnection(cfg.PeachEbillsConnectionString))
        {
            cn.Open();
            using var cmd = new SqlCommand("SELECT TOP 1 Driver, DSN, uid, pwd, servername, dbq FROM PeachConnString WHERE RUC = @ruc", cn);
            cmd.Parameters.AddWithValue("@ruc", ruc);
            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) throw new RechazoTrabajoException($"El RUC {ruc} no tiene fila en PeachEBills.PeachConnString.");
            driver = rd.GetString(0).Trim();
            dsn = rd.IsDBNull(1) ? string.Empty : rd.GetString(1).Trim();
            uid = rd.GetString(2).Trim();
            pwd = Descifrar(rd.IsDBNull(3) ? null : rd.GetString(3));
            servidor = rd.IsDBNull(4) ? null : rd.GetString(4).Trim();
            dbq = rd.IsDBNull(5) ? null : rd.GetString(5).Trim();
        }

        if (!string.IsNullOrWhiteSpace(cfg.ServidorSageOverride)) servidor = cfg.ServidorSageOverride!.Trim();
        if (!string.IsNullOrWhiteSpace(dbq)) dbq = cfg.BaseParaRuc(ruc, dbq!);
        var cadena = !string.IsNullOrEmpty(servidor) && !string.IsNullOrEmpty(dbq)
            ? $"Driver={driver};servername={servidor};uid={uid};dbq={dbq};pwd={pwd};"
            : $"Dsn={dsn};Driver={driver};uid={uid};pwd={pwd};";
        return new OdbcSage(cadena);
    }

    /// <summary>OC de un proveedor con ese nº de factura (<c>WasPreviewCreated</c>): PostOrder, referencia y nº de retención.</summary>
    public (int PostOrder, string Referencia, string Retencion)? OcExistente(string vendorId, string numeroFactura)
    {
        using var cmd = Comando(
            "SELECT h.PostOrder, h.Reference, h.ShipToAddress2 FROM JrnlHdr h, Vendors v " +
            "WHERE h.CustVendId = v.VendorRecordNumber AND h.JrnlKey_Journal = 10 AND h.JournalEx = 18 AND v.VendorID = ? AND h.TermsDescription = ? " +
            "ORDER BY h.PostOrder", vendorId, numeroFactura);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        return (Convert.ToInt32(rd.GetValue(0)), Texto(rd.GetValue(1)), Texto(rd.GetValue(2)));
    }

    /// <summary>¿La OC ya se recibió (convertida en compra)? (<c>WasPicked</c>: alguna fila con <c>StockingQtyReceived</c> ≠ 0).</summary>
    public bool FueRecibida(int postOrder)
    {
        using var cmd = Comando("SELECT COUNT(*) FROM JrnlRow WHERE PostOrder = ? AND StockingQtyReceived <> 0", postOrder);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public List<string> ReferenciasOc(string prefijo) => Lista(
        $"SELECT Reference FROM JrnlHdr WHERE {FiltroOc} AND Reference LIKE ?", prefijo + "-%");

    public List<string> NumerosRetencion(string serie) => Lista(
        $"SELECT ShipToAddress2 FROM JrnlHdr WHERE {FiltroOc} AND ShipToAddress2 LIKE ?", serie + "-%");

    /// <summary>OC (otra que <paramref name="excepto"/>) que ya usa ese nº de retención (<c>CheckForTwhNumberAlreadyUsed</c>).</summary>
    public string? OcConRetencion(string numeroRetencion, int excepto)
    {
        using var cmd = Comando(
            $"SELECT Reference, TermsDescription FROM JrnlHdr WHERE {FiltroOc} AND ShipToAddress2 = ? AND PostOrder <> ?",
            numeroRetencion, excepto);
        using var rd = cmd.ExecuteReader();
        return rd.Read() ? $"{Texto(rd.GetValue(0))} (factura {Texto(rd.GetValue(1))})" : null;
    }

    /// <summary>
    /// OC pendientes de convertir en compra: el mismo filtro que el worker COM (<c>Commons.QueryForPurchaseOrdersNotInInvoice</c>):
    /// alguna fila con ítem y cantidad sin recibir, <c>ShipToAddress1</c> (nº de factura) no vacío, no <c>ANULAD%</c>, emitida desde
    /// <paramref name="desde"/>.
    /// </summary>
    public List<OcPendiente> OcsPendientesDeCompra(DateTime desde)
    {
        var lista = new List<OcPendiente>();
        using var cmd = Comando(
            "SELECT DISTINCT h.PostOrder, h.Reference, h.TransactionDate, h.GoodThruDate, h.ShipVia, h.ShipToAddress1, h.ShipToAddress2, " +
            "h.ShipToState, h.CustVendId, v.VendorID, ap.AccountID " +
            "FROM JrnlHdr h, JrnlRow r, LineItem l, Vendors v, Chart ap " +
            "WHERE h.PostOrder = r.PostOrder AND r.ItemRecordNumber = l.ItemRecordNumber AND h.CustVendId = v.VendorRecordNumber " +
            "AND h.GLAcntNumber = ap.GLAcntNumber AND h.JrnlKey_Journal = 10 AND h.JournalEx = 18 " +
            "AND ROUND(r.StockingQtyReceived, 2) < ROUND(r.Quantity, 2) AND LENGTH(h.ShipToAddress1) > 0 " +
            "AND NOT (h.Description LIKE 'ANULAD%') AND h.TransactionDate >= ?", desde.Date);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            lista.Add(new OcPendiente
            {
                PostOrder = Convert.ToInt32(rd.GetValue(0)),
                Referencia = Texto(rd.GetValue(1)),
                Fecha = Convert.ToDateTime(rd.GetValue(2)),
                FechaRegistro = rd.IsDBNull(3) ? (DateTime?)null : Convert.ToDateTime(rd.GetValue(3)),
                ShipVia = Texto(rd.GetValue(4)),
                Direccion1 = Texto(rd.GetValue(5)),
                Direccion2 = Texto(rd.GetValue(6)),
                Estado = Texto(rd.GetValue(7)),
                VendorRecord = Convert.ToInt32(rd.GetValue(8)),
                VendorId = Texto(rd.GetValue(9)),
                CuentaPorPagar = Texto(rd.GetValue(10)),
            });
        }
        return lista;
    }

    /// <summary>La compra que recibe esa OC (<c>INV_POSOOrderNumber</c> = nº de OC, mismo proveedor), como la busca el worker.</summary>
    public int? CompraDeOc(string referenciaOc, int vendorRecord)
    {
        using var cmd = Comando("SELECT PostOrder FROM JrnlHdr WHERE JrnlKey_Journal = 4 AND INV_POSOOrderNumber = ? AND CustVendId = ? ORDER BY PostOrder DESC",
            referenciaOc, vendorRecord);
        var r = cmd.ExecuteScalar();
        return r is null or DBNull ? null : Convert.ToInt32(r);
    }

    public bool ExisteReferenciaOc(string referencia)
    {
        using var cmd = Comando($"SELECT COUNT(*) FROM JrnlHdr WHERE {FiltroOc} AND Reference = ?", referencia);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>Cabecera de una OC por PostOrder: referencia, fecha, proveedor y cuenta por pagar.</summary>
    public OcPendiente? Oc(int postOrder)
    {
        using var cmd = Comando(
            "SELECT h.PostOrder, h.Reference, h.TransactionDate, h.CustVendId, v.VendorID, ap.AccountID " +
            "FROM JrnlHdr h, Vendors v, Chart ap WHERE h.CustVendId = v.VendorRecordNumber AND h.GLAcntNumber = ap.GLAcntNumber " +
            "AND h.JrnlKey_Journal = 10 AND h.JournalEx = 18 AND h.PostOrder = ?", postOrder);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        return new OcPendiente
        {
            PostOrder = Convert.ToInt32(rd.GetValue(0)),
            Referencia = Texto(rd.GetValue(1)),
            Fecha = Convert.ToDateTime(rd.GetValue(2)),
            VendorRecord = Convert.ToInt32(rd.GetValue(3)),
            VendorId = Texto(rd.GetValue(4)),
            CuentaPorPagar = Texto(rd.GetValue(5)),
        };
    }

    /// <summary>Compra del proveedor con esa referencia (la de una liquidación cuyas líneas no quedaron aplicadas a la OC).</summary>
    public int? CompraPorReferencia(string referencia, int vendorRecord)
    {
        using var cmd = Comando("SELECT PostOrder FROM JrnlHdr WHERE JrnlKey_Journal = 4 AND JournalEx = 11 AND Reference = ? AND CustVendId = ? ORDER BY PostOrder DESC",
            referencia, vendorRecord);
        var r = cmd.ExecuteScalar();
        return r is null or DBNull ? null : Convert.ToInt32(r);
    }

    /// <summary>PostOrder de la OC con esa referencia (la más reciente).</summary>
    public int? OcPorReferencia(string referencia)
    {
        using var cmd = Comando($"SELECT PostOrder FROM JrnlHdr WHERE {FiltroOc} AND Reference = ? ORDER BY PostOrder DESC", referencia);
        var r = cmd.ExecuteScalar();
        return r is null or DBNull ? null : Convert.ToInt32(r);
    }

    private List<string> Lista(string sql, params object[] parametros)
    {
        var lista = new List<string>();
        using var cmd = Comando(sql, parametros);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) lista.Add(Texto(rd.GetValue(0)));
        return lista;
    }

    private OdbcCommand Comando(string sql, params object[] parametros)
    {
        var cmd = new OdbcCommand(sql, _cn);
        foreach (var p in parametros)
        {
            cmd.Parameters.Add(p switch
            {
                int n => new OdbcParameter { OdbcType = OdbcType.Int, Value = n },
                DateTime d => new OdbcParameter { OdbcType = OdbcType.Date, Value = d },
                _ => new OdbcParameter { OdbcType = OdbcType.VarChar, Value = p },
            });
        }
        return cmd;
    }

    private static string Texto(object? v) => v is null or DBNull ? string.Empty : Convert.ToString(v)!.Trim();

    /// <summary>
    /// Contraseña de <c>PeachConnString.pwd</c>: TripleDES/ECB/PKCS7 con clave MD5("1oo2435681"), el <c>PasswordSecurity</c>
    /// original (mismo algoritmo que <c>PsaWeb.PeachEbills.DbSecret</c>).
    /// </summary>
    private static string Descifrar(string? base64)
    {
        if (string.IsNullOrEmpty(base64)) return string.Empty;
        using var md5 = MD5.Create();
        using var tdes = new TripleDESCryptoServiceProvider
        {
            Key = md5.ComputeHash(Encoding.UTF8.GetBytes("1oo2435681")),
            Mode = CipherMode.ECB,
            Padding = PaddingMode.PKCS7,
        };
        using var t = tdes.CreateDecryptor();
        var entrada = Convert.FromBase64String(base64);
        return Encoding.UTF8.GetString(t.TransformFinalBlock(entrada, 0, entrada.Length));
    }

    public void Dispose() => _cn.Dispose();
}

/// <summary>Cabecera de una OC pendiente de convertir (lo que el worker copiaba a la compra).</summary>
public sealed class OcPendiente
{
    public int PostOrder { get; set; }
    public string Referencia { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public DateTime? FechaRegistro { get; set; }
    public string ShipVia { get; set; } = string.Empty;
    public string Direccion1 { get; set; } = string.Empty;
    public string Direccion2 { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public int VendorRecord { get; set; }
    public string VendorId { get; set; } = string.Empty;
    public string CuentaPorPagar { get; set; } = string.Empty;
}

/// <summary>
/// <c>PeachEBills.PurchaseOrderSync</c> (OC → compra por RUC): lo que el worker escribía y de lo que depende el módulo web de
/// Retenciones para las pendientes.
/// </summary>
public sealed class SincronizacionCompras
{
    private readonly string _cs;

    public SincronizacionCompras(string peachEbillsConnectionString) => _cs = peachEbillsConnectionString;

    public HashSet<int> OcsSincronizadas(string ruc)
    {
        var r = new HashSet<int>();
        using var cn = new SqlConnection(_cs);
        cn.Open();
        using var cmd = new SqlCommand("SELECT POPostOrder FROM PurchaseOrderSync WHERE RUCTransmitter = @ruc", cn);
        cmd.Parameters.AddWithValue("@ruc", ruc);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (int.TryParse(rd.GetString(0).Trim(), out var po)) r.Add(po);
        }
        return r;
    }

    /// <summary>Anota OC → compra (idempotente: no duplica la OC).</summary>
    public void Anotar(string ruc, int postOrderOc, int postOrderCompra)
    {
        using var cn = new SqlConnection(_cs);
        cn.Open();
        using var cmd = new SqlCommand(
            @"IF NOT EXISTS (SELECT 1 FROM PurchaseOrderSync WHERE RUCTransmitter = @ruc AND POPostOrder = @po)
                INSERT INTO PurchaseOrderSync (POPostOrder, PIPostOrder, RUCTransmitter) VALUES (@po, @pi, @ruc)", cn);
        cmd.Parameters.AddWithValue("@ruc", ruc);
        cmd.Parameters.AddWithValue("@po", postOrderOc.ToString(System.Globalization.CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@pi", postOrderCompra.ToString(System.Globalization.CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }
}

/// <summary>El trabajo no se puede hacer con esos datos (validación de negocio o de Sage): queda en Error, sin reintentos.</summary>
public sealed class RechazoTrabajoException : Exception
{
    public RechazoTrabajoException(string mensaje) : base(mensaje) { }
}
