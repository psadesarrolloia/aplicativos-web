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

    public bool ExisteReferenciaOc(string referencia)
    {
        using var cmd = Comando($"SELECT COUNT(*) FROM JrnlHdr WHERE {FiltroOc} AND Reference = ?", referencia);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
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
            cmd.Parameters.Add(p is int n
                ? new OdbcParameter { OdbcType = OdbcType.Int, Value = n }
                : new OdbcParameter { OdbcType = OdbcType.VarChar, Value = p });
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

/// <summary>El trabajo no se puede hacer con esos datos (validación de negocio o de Sage): queda en Error, sin reintentos.</summary>
public sealed class RechazoTrabajoException : Exception
{
    public RechazoTrabajoException(string mensaje) : base(mensaje) { }
}
