using System;
using System.Data.SqlClient;

namespace PsaWeb.SageBridge.Logica;

/// <summary>Servidor y base de Sage de una empresa.</summary>
public sealed class EmpresaSage
{
    public string Ruc { get; set; } = string.Empty;
    public string? Servidor { get; set; }
    public string BaseDatos { get; set; } = string.Empty;
}

/// <summary>
/// Resuelve la compañía de Sage de un RUC desde <c>PeachEBills.PeachConnString</c> (<c>servername</c>, <c>dbq</c>):
/// la misma fuente que el exe antiguo y la web. No lee la contraseña ODBC: el SDK no la necesita.
/// </summary>
public sealed class ResolutorEmpresas
{
    private readonly Configuracion _cfg;

    public ResolutorEmpresas(Configuracion cfg) => _cfg = cfg;

    public EmpresaSage Resolver(string ruc)
    {
        string? servidor;
        string? dbq;
        using (var cn = new SqlConnection(_cfg.PeachEbillsConnectionString))
        {
            cn.Open();
            using var cmd = new SqlCommand("SELECT TOP 1 servername, dbq FROM PeachConnString WHERE RUC = @ruc", cn);
            cmd.Parameters.AddWithValue("@ruc", ruc);
            using var rd = cmd.ExecuteReader();
            if (!rd.Read())
            {
                throw new InvalidOperationException($"El RUC {ruc} no tiene fila en PeachEBills.PeachConnString.");
            }

            servidor = rd.IsDBNull(0) ? null : rd.GetString(0).Trim();
            dbq = rd.IsDBNull(1) ? null : rd.GetString(1).Trim();
        }

        if (string.IsNullOrWhiteSpace(dbq))
        {
            throw new InvalidOperationException($"El RUC {ruc} no tiene base (dbq) configurada en PeachConnString.");
        }

        return new EmpresaSage
        {
            Ruc = ruc,
            Servidor = string.IsNullOrWhiteSpace(_cfg.ServidorSageOverride) ? servidor : _cfg.ServidorSageOverride!.Trim(),
            BaseDatos = _cfg.BaseParaRuc(ruc, dbq!),
        };
    }
}
