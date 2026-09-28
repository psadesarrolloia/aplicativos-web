using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using PsaWeb.SageBridge.Contratos;

namespace PsaWeb.SageBridge.Logica;

/// <summary>Un trabajo tomado de la cola por esta instancia.</summary>
public sealed class TrabajoTomado
{
    public long Id { get; set; }
    public string Ruc { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public string ClaveIdempotencia { get; set; } = string.Empty;
    public int Intentos { get; set; }
}

/// <summary>Empresa con trabajos listos para tomar.</summary>
public sealed class EmpresaConPendientes
{
    public string Ruc { get; set; } = string.Empty;
    public long PrimerId { get; set; }
    public bool HayProbar { get; set; }
}

public sealed class ConfigEmpresa
{
    public bool Habilitada { get; set; }
    public string? Ventana { get; set; }
}

/// <summary>
/// Acceso del Bridge a las tablas de la cola en <c>PsaWebPlataforma</c> (esquema dueño: <c>SageBridgeDbContext</c>
/// del lado web). Cada operación abre y cierra su conexión. Las actualizaciones de un trabajo exigen que siga
/// tomado por esta instancia (<c>TomadoPor</c>), para no pisar a otra que lo haya retomado tras vencer el lease.
/// </summary>
public sealed class ColaSql
{
    private const int LargoError = 2000;
    private readonly string _cs;

    public ColaSql(string connectionString) => _cs = connectionString;

    private const string CondicionListo = @"
        ((t.Estado = 'EnCola' AND (t.NoAntesDeUtc IS NULL OR t.NoAntesDeUtc <= @ahora))
         OR (t.Estado = 'EnProceso' AND t.TomadoHastaUtc < @ahora))";

    /// <summary>Al arrancar: devuelve a la cola lo que esta instancia dejó tomado (se cayó a mitad de un lote).</summary>
    public int LiberarPropios(string instancia) => Ejecutar(
        @"UPDATE TrabajosSage SET Estado = 'EnCola', TomadoPor = NULL, TomadoHastaUtc = NULL
          WHERE Estado = 'EnProceso' AND TomadoPor = @inst",
        P("@inst", instancia));

    public void Latir(string instancia, DateTime iniciadoUtc, string usuario, string versionAnfitrion, string versionLogica,
        string estado, string? empresaAbierta) => Ejecutar(
        @"MERGE LatidosBridge WITH (HOLDLOCK) AS d
          USING (SELECT @inst AS Instancia) AS o ON d.Instancia = o.Instancia
          WHEN MATCHED THEN UPDATE SET UltimoLatidoUtc = SYSUTCDATETIME(), IniciadoUtc = @ini, Usuario = @usr,
               VersionAnfitrion = @va, VersionLogica = @vl, Estado = @est, EmpresaAbierta = @emp
          WHEN NOT MATCHED THEN INSERT (Instancia, UltimoLatidoUtc, IniciadoUtc, Usuario, VersionAnfitrion, VersionLogica, Estado, EmpresaAbierta)
               VALUES (@inst, SYSUTCDATETIME(), @ini, @usr, @va, @vl, @est, @emp);",
        P("@inst", instancia), P("@ini", iniciadoUtc), P("@usr", Cortar(usuario, 100)), P("@va", Cortar(versionAnfitrion, 40)),
        P("@vl", Cortar(versionLogica, 40)), P("@est", Cortar(estado, 300)), P("@emp", empresaAbierta));

    public Dictionary<string, ConfigEmpresa> LeerEmpresas()
    {
        var r = new Dictionary<string, ConfigEmpresa>();
        using var cn = Abrir();
        using var cmd = new SqlCommand("SELECT Ruc, Habilitada, Ventana FROM EmpresasBridge", cn);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            r[rd.GetString(0)] = new ConfigEmpresa { Habilitada = rd.GetBoolean(1), Ventana = rd.IsDBNull(2) ? null : rd.GetString(2) };
        }

        return r;
    }

    public List<EmpresaConPendientes> EmpresasConPendientes(DateTime ahoraUtc)
    {
        var r = new List<EmpresaConPendientes>();
        using var cn = Abrir();
        using var cmd = new SqlCommand(
            $@"SELECT t.Ruc, MIN(t.Id), MAX(CASE WHEN t.Tipo = '{TiposTrabajo.ProbarEmpresa}' THEN 1 ELSE 0 END)
               FROM TrabajosSage t WHERE {CondicionListo}
               GROUP BY t.Ruc ORDER BY MIN(t.Id)", cn);
        cmd.Parameters.Add(P("@ahora", ahoraUtc));
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            r.Add(new EmpresaConPendientes { Ruc = rd.GetString(0), PrimerId = rd.GetInt64(1), HayProbar = rd.GetInt32(2) == 1 });
        }

        return r;
    }

    /// <summary>Toma hasta <paramref name="maximo"/> trabajos listos de una empresa (lease hasta <paramref name="leaseHastaUtc"/>).</summary>
    public List<TrabajoTomado> TomarLote(string ruc, string instancia, DateTime ahoraUtc, DateTime leaseHastaUtc, int maximo, bool soloProbar)
    {
        var r = new List<TrabajoTomado>();
        using var cn = Abrir();
        using var cmd = new SqlCommand(
            $@"WITH c AS (
                 SELECT TOP (@max) * FROM TrabajosSage t WITH (ROWLOCK, UPDLOCK, READPAST)
                 WHERE t.Ruc = @ruc AND {CondicionListo}
                   AND (@soloProbar = 0 OR t.Tipo = '{TiposTrabajo.ProbarEmpresa}')
                 ORDER BY t.Id)
               UPDATE c SET Estado = 'EnProceso', TomadoPor = @inst, TomadoHastaUtc = @lease, IniciadoUtc = @ahora,
                            Intentos = Intentos + 1
               OUTPUT inserted.Id, inserted.Tipo, inserted.PayloadJson, inserted.ClaveIdempotencia, inserted.Intentos;", cn);
        cmd.Parameters.Add(P("@max", maximo));
        cmd.Parameters.Add(P("@ruc", ruc));
        cmd.Parameters.Add(P("@ahora", ahoraUtc));
        cmd.Parameters.Add(P("@lease", leaseHastaUtc));
        cmd.Parameters.Add(P("@inst", instancia));
        cmd.Parameters.Add(P("@soloProbar", soloProbar ? 1 : 0));
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            r.Add(new TrabajoTomado
            {
                Id = rd.GetInt64(0), Ruc = ruc, Tipo = rd.GetString(1), PayloadJson = rd.IsDBNull(2) ? null : rd.GetString(2),
                ClaveIdempotencia = rd.GetString(3), Intentos = rd.GetInt32(4),
            });
        }

        return r.OrderBy(t => t.Id).ToList();
    }

    public void RenovarLease(long id, string instancia, DateTime leaseHastaUtc) => Ejecutar(
        "UPDATE TrabajosSage SET TomadoHastaUtc = @lease WHERE Id = @id AND TomadoPor = @inst AND Estado = 'EnProceso'",
        P("@lease", leaseHastaUtc), P("@id", id), P("@inst", instancia));

    public void Completar(long id, string instancia, string resultadoJson) => Ejecutar(
        @"UPDATE TrabajosSage SET Estado = 'Hecho', ResultadoJson = @res, Error = NULL, TerminadoUtc = SYSUTCDATETIME(),
                 TomadoHastaUtc = NULL
          WHERE Id = @id AND TomadoPor = @inst AND Estado = 'EnProceso'",
        P("@res", resultadoJson), P("@id", id), P("@inst", instancia));

    public void Fallar(long id, string instancia, string error) => Ejecutar(
        @"UPDATE TrabajosSage SET Estado = 'Error', Error = @err, TerminadoUtc = SYSUTCDATETIME(), TomadoHastaUtc = NULL
          WHERE Id = @id AND TomadoPor = @inst AND Estado = 'EnProceso'",
        P("@err", Cortar(error, LargoError)), P("@id", id), P("@inst", instancia));

    /// <summary>Vuelve a la cola para reintentar desde <paramref name="noAntesDeUtc"/> (nulo = de inmediato).</summary>
    public void Reprogramar(long id, string instancia, string? error, DateTime? noAntesDeUtc, bool descontarIntento = false) => Ejecutar(
        @"UPDATE TrabajosSage SET Estado = 'EnCola', Error = @err, NoAntesDeUtc = @nad, TomadoPor = NULL, TomadoHastaUtc = NULL,
                 Intentos = CASE WHEN @desc = 1 AND Intentos > 0 THEN Intentos - 1 ELSE Intentos END
          WHERE Id = @id AND TomadoPor = @inst AND Estado = 'EnProceso'",
        P("@err", error is null ? null : Cortar(error, LargoError)), P("@nad", noAntesDeUtc), P("@desc", descontarIntento ? 1 : 0),
        P("@id", id), P("@inst", instancia));

    /// <summary>Anota el último resultado de <c>VerifyAccess</c> de la empresa (crea la fila no habilitada si no existe).</summary>
    public void GuardarAcceso(string ruc, string acceso) => Ejecutar(
        @"MERGE EmpresasBridge WITH (HOLDLOCK) AS d
          USING (SELECT @ruc AS Ruc) AS o ON d.Ruc = o.Ruc
          WHEN MATCHED THEN UPDATE SET AccesoSage = @acc, AccesoVerificadoUtc = SYSUTCDATETIME()
          WHEN NOT MATCHED THEN INSERT (Ruc, Habilitada, AccesoSage, AccesoVerificadoUtc, ModificadoPor, ModificadoUtc)
               VALUES (@ruc, 0, @acc, SYSUTCDATETIME(), 'SageBridge', SYSUTCDATETIME());",
        P("@ruc", ruc), P("@acc", Cortar(acceso, 30)));

    /// <summary>
    /// Encola un trabajo del propio Bridge (conversión OC → compra) si la empresa no tiene ya uno de ese tipo en cola o en
    /// proceso. Devuelve si lo encoló.
    /// </summary>
    public bool EncolarSiNoHayPendiente(string ruc, string tipo, string? payloadJson, string clave, string creadoPor)
    {
        try
        {
            return Ejecutar(
                @"INSERT INTO TrabajosSage (Ruc, Tipo, Estado, ClaveIdempotencia, PayloadJson, Intentos, CreadoPor, CreadoUtc)
                  SELECT @ruc, @tipo, 'EnCola', @clave, @payload, 0, @por, SYSUTCDATETIME()
                  WHERE NOT EXISTS (SELECT 1 FROM TrabajosSage WITH (UPDLOCK, HOLDLOCK)
                                    WHERE Ruc = @ruc AND Tipo = @tipo AND Estado IN ('EnCola', 'EnProceso'))",
                P("@ruc", ruc), P("@tipo", tipo), P("@clave", clave), P("@payload", payloadJson), P("@por", creadoPor)) > 0;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return false; // misma clave ya usada
        }
    }

    public void ProbarConexion()
    {
        using var cn = Abrir();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM TrabajosSage", cn);
        cmd.ExecuteScalar();
    }

    private SqlConnection Abrir()
    {
        var cn = new SqlConnection(_cs);
        cn.Open();
        return cn;
    }

    private int Ejecutar(string sql, params SqlParameter[] parametros)
    {
        using var cn = Abrir();
        using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddRange(parametros);
        return cmd.ExecuteNonQuery();
    }

    private static SqlParameter P(string nombre, object? valor)
    {
        var p = new SqlParameter(nombre, valor ?? DBNull.Value);
        if (valor is DateTime) p.SqlDbType = SqlDbType.DateTime2;
        return p;
    }

    private static string Cortar(string texto, int largo) => texto.Length <= largo ? texto : texto.Substring(0, largo);
}
