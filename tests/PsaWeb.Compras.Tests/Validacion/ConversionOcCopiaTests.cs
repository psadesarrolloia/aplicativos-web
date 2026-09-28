using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PsaWeb.SageBridge.Cola;
using PsaWeb.SageBridge.Cola.Data;
using PsaWeb.SageBridge.Contratos;
using Xunit.Abstractions;

namespace PsaWeb.Compras.Tests.Validacion;

/// <summary>
/// Cierre de la F6 (§20 del plan de la Ola 2): <b>ESCRIBE en la copia de prueba</b>. Convierte en compra, por el trabajo
/// <see cref="TiposTrabajo.ConvertirOcs"/> del Bridge, las OC de prueba de la F3 (factura <c>999-998-&lt;PostOrder original&gt;</c>)
/// y compara por ODBC <b>todas</b> las columnas de <c>JrnlHdr</c>/<c>JrnlRow</c> de cada compra con la que hizo el worker COM
/// para la OC original. Verifica además la fila de <c>PeachEBills.PurchaseOrderSync</c>.
/// <para>
/// Solo corre con <c>PSAWEB_TEST_F6_CONVERTIR=1</c>, <c>PSAWEB_TEST_SAGE_COMPRAS</c> y el Bridge en consola. Habilita la
/// empresa mientras dura y la deshabilita al final. Si la conversión periódica del Bridge se adelanta, se compara igual
/// (la compra se busca por la OC, no por el trabajo).
/// </para>
/// </summary>
public class ConversionOcCopiaTests(ITestOutputHelper salida)
{
    /// <summary>
    /// Columnas que difieren por diseño (§20 del plan): identidad/grabado, los datos de prueba (nº de factura, de OC y de
    /// retención), el enlace a otra OC (<c>LinkToAnotherTrx</c>) y <c>IncludeInInvLedger</c> (el SDK no lo expone: 0 en vez de 1).
    /// </summary>
    private static readonly HashSet<string> ColumnasEsperadas = new(StringComparer.OrdinalIgnoreCase)
    {
        "PostOrder", "Reference", "TermsDescription", "ShipToAddress1", "ShipToAddress2", "INV_POSOOrderNumber", "InvNumForThisTrx",
        "JrnlKey_TrxNumber", "LastPostedAt", "LastUpdateCounter", "rGUIDa", "rGUIDb", "rGUIDc", "rGUIDd",
        "LinkToAnotherTrx", "IncludeInInvLedger",
    };

    /// <summary>Pagos posteriores a la compra del worker.</summary>
    private static readonly HashSet<string> ColumnasDePago = new(StringComparer.OrdinalIgnoreCase) { "AmountPaid", "CompletedDate", "LastActivityDate" };

    /// <summary>Lo que contabilidad suele corregir a mano en la compra ya grabada (cuenta, nombre en la cabecera).</summary>
    private static readonly HashSet<string> ColumnasEditables = new(StringComparer.OrdinalIgnoreCase) { "GLAcntNumber", "Description" };

    private sealed class FabricaCola : IDbContextFactory<SageBridgeDbContext>
    {
        public SageBridgeDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<SageBridgeDbContext>().UseSqlServer(EntornoCompras.PlataformaLocal).Options);
    }

    [SkippableFact]
    public async Task Las_compras_del_bridge_son_iguales_a_las_del_worker()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("PSAWEB_TEST_F6_CONVERTIR") == "1", "Escribe en la copia de prueba: solo con PSAWEB_TEST_F6_CONVERTIR=1.");
        Skip.If(EntornoCompras.CadenaSage is null, "Sin PSAWEB_TEST_SAGE_COMPRAS.");
        Skip.If(Environment.Is64BitProcess, "El driver ODBC de Pervasive es de 32 bits.");
        var cola = new ColaSage(new FabricaCola());
        Skip.IfNot((await cola.LatidosAsync()).Any(l => EstadoBridge.EstaVivo(l, DateTime.UtcNow)), "El Bridge no está corriendo (PsaWeb.SageBridge.exe --consola).");

        await using var cn = new OdbcConnection(EntornoCompras.CadenaSage);
        await cn.OpenAsync();

        // OC de prueba de la F3 → OC original del .exe (la que el worker ya convirtió).
        var prueba = new List<(int PostOrder, string Referencia, int VendorRecord, int Original)>();
        foreach (DataRow r in Tabla(cn, "SELECT PostOrder, Reference, CustVendId, ShipToAddress1 FROM JrnlHdr " +
                                        "WHERE JrnlKey_Journal = 10 AND JournalEx = 18 AND ShipToAddress1 LIKE '999-998-%' ORDER BY PostOrder").Rows)
        {
            prueba.Add((Convert.ToInt32(r[0]), T(r[1]), Convert.ToInt32(r[2]), int.Parse(T(r[3])[8..], CultureInfo.InvariantCulture)));
        }
        salida.WriteLine($"OC de prueba: {prueba.Count} ({string.Join(", ", prueba.Select(p => p.Referencia))})");
        Assert.NotEmpty(prueba);

        var informe = new StringBuilder();
        var fallas = 0;
        await cola.GuardarEmpresaAsync(EntornoCompras.Ruc, true, null, "Validación F6 (arnés)", "test-f6");
        try
        {
            var payload = JsonSerializer.Serialize(new PayloadConvertirOcs { PostOrders = prueba.Select(p => p.PostOrder).ToList() });
            var id = (await cola.EncolarAsync(EntornoCompras.Ruc, TiposTrabajo.ConvertirOcs, payload, $"test-f6-{DateTime.UtcNow:yyyyMMddHHmmss}", "test-f6")).Trabajo.Id;
            var t = await EsperarAsync(cola, id);
            informe.AppendLine($"Trabajo #{id}: {t.Estado} {t.Error}");
            informe.AppendLine(t.ResultadoJson);
            if (t.Estado != EstadosTrabajo.Hecho) fallas++;

            // Idempotencia: una segunda corrida no encuentra nada (ya están en PurchaseOrderSync y recibidas).
            var id2 = (await cola.EncolarAsync(EntornoCompras.Ruc, TiposTrabajo.ConvertirOcs, payload, $"test-f6b-{DateTime.UtcNow:yyyyMMddHHmmss}", "test-f6")).Trabajo.Id;
            var t2 = await EsperarAsync(cola, id2);
            var r2 = t2.ResultadoJson is null ? null : JsonSerializer.Deserialize<ResultadoConvertirOcs>(t2.ResultadoJson);
            var idem = t2.Estado == EstadosTrabajo.Hecho && r2!.Pendientes == 0;
            informe.AppendLine($"== Segunda corrida: {(idem ? "OK" : "FALLA")} ({t2.Estado} {t2.ResultadoJson})");
            if (!idem) fallas++;
        }
        finally
        {
            await cola.GuardarEmpresaAsync(EntornoCompras.Ruc, false, null, "Validación F6 terminada", "test-f6");
        }

        var sync = LeerSync(prueba.Select(p => p.PostOrder));
        foreach (var p in prueba)
        {
            var nueva = Compra(cn, p.Referencia, p.VendorRecord);
            var original = Tabla(cn, $"SELECT Reference, CustVendId FROM JrnlHdr WHERE PostOrder = {p.Original}").Rows[0];
            var refOriginal = T(original[0]);
            var delWorker = Compra(cn, refOriginal, Convert.ToInt32(original[1], CultureInfo.InvariantCulture));
            if (nueva is null || delWorker is null)
            {
                fallas++;
                informe.AppendLine($"== {p.Referencia}: compra del bridge {nueva?.ToString() ?? "NO"} / del worker {delWorker?.ToString() ?? "NO"}");
                continue;
            }

            var difs = Comparar(cn, delWorker.Value, nueva.Value);
            var anotada = sync.TryGetValue(p.PostOrder, out var pi) && pi == nueva.Value;
            informe.AppendLine($"== {p.Referencia} ← {refOriginal}: compra {nueva} (worker {delWorker}), PurchaseOrderSync {(anotada ? "OK" : "FALTA")}, {difs.Count} diferencias");
            foreach (var d in difs) informe.AppendLine("   " + d);
            if (!anotada || difs.Count > 0) fallas++;
        }

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "psa-f6-conversion.txt"), informe.ToString());
        salida.WriteLine(informe.ToString());
        Assert.True(fallas == 0, informe.ToString());
    }

    private static int? Compra(OdbcConnection cn, string referenciaOc, int vendorRecord)
    {
        using var cmd = new OdbcCommand("SELECT PostOrder FROM JrnlHdr WHERE JrnlKey_Journal = 4 AND INV_POSOOrderNumber = ? AND CustVendId = ? ORDER BY PostOrder DESC", cn);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = referenciaOc });
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = vendorRecord });
        var r = cmd.ExecuteScalar();
        return r is null or DBNull ? null : Convert.ToInt32(r, CultureInfo.InvariantCulture);
    }

    private static Dictionary<int, int> LeerSync(IEnumerable<int> ocs)
    {
        var r = new Dictionary<int, int>();
        using var cn = new SqlConnection(EntornoCompras.PeachEbillsLocal);
        cn.Open();
        using var cmd = new SqlCommand($"SELECT POPostOrder, PIPostOrder FROM PurchaseOrderSync WHERE RUCTransmitter = @ruc AND POPostOrder IN ({string.Join(",", ocs.Select(o => $"'{o}'"))})", cn);
        cmd.Parameters.AddWithValue("@ruc", EntornoCompras.Ruc);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) r[int.Parse(rd.GetString(0).Trim(), CultureInfo.InvariantCulture)] = int.Parse(rd.GetString(1).Trim(), CultureInfo.InvariantCulture);
        return r;
    }

    /// <summary>Todas las columnas de <c>JrnlHdr</c> y de cada fila de <c>JrnlRow</c>, salvo <see cref="ColumnasEsperadas"/>.</summary>
    private static List<string> Comparar(OdbcConnection cn, int delWorker, int nueva)
    {
        var difs = new List<string>();
        var ha = Tabla(cn, $"SELECT * FROM JrnlHdr WHERE PostOrder = {delWorker}");
        var hb = Tabla(cn, $"SELECT * FROM JrnlHdr WHERE PostOrder = {nueva}");
        var editada = Convert.ToInt32(ha.Rows[0]["LastUpdateCounter"], CultureInfo.InvariantCulture) > 1;
        var pagada = V(ha.Rows[0], "AmountPaid") != V(hb.Rows[0], "AmountPaid");
        // La OC de prueba se actualizó en el lugar en la F3: sus distribuciones están renumeradas.
        var ocActualizada = false;
        foreach (DataColumn c in ha.Columns)
        {
            if (ColumnasEsperadas.Contains(c.ColumnName)) continue;
            if (pagada && ColumnasDePago.Contains(c.ColumnName)) continue;
            if (editada && ColumnasEditables.Contains(c.ColumnName)) continue;
            var a = V(ha.Rows[0], c.ColumnName);
            var b = V(hb.Rows[0], c.ColumnName);
            if (a != b) difs.Add($"JrnlHdr.{c.ColumnName}: worker «{a}» / bridge «{b}»");
        }
        var fa = Tabla(cn, $"SELECT * FROM JrnlRow WHERE PostOrder = {delWorker} ORDER BY RowNumber");
        var fb = Tabla(cn, $"SELECT * FROM JrnlRow WHERE PostOrder = {nueva} ORDER BY RowNumber");
        if (fa.Rows.Count != fb.Rows.Count) difs.Add($"Filas: worker {fa.Rows.Count} / bridge {fb.Rows.Count}");
        for (var i = 0; i < Math.Min(fa.Rows.Count, fb.Rows.Count); i++)
        {
            if (i == 1) ocActualizada = V(fa.Rows[i], "LinkToOtherTrxIndex") != V(fb.Rows[i], "LinkToOtherTrxIndex");
            foreach (DataColumn c in fa.Columns)
            {
                if (ColumnasEsperadas.Contains(c.ColumnName)) continue;
                if (editada && ColumnasEditables.Contains(c.ColumnName)) continue;
                if (ocActualizada && c.ColumnName == "LinkToOtherTrxIndex") continue;
                var a = V(fa.Rows[i], c.ColumnName);
                var b = V(fb.Rows[i], c.ColumnName);
                if (a == b) continue;
                // Fila 0 (cuenta por pagar): el worker cortaba el nombre del proveedor a 30; el Bridge lo deja completo.
                if (i == 0 && c.ColumnName == "RowDescription" && ((a.Length == 30 && b.StartsWith(a, StringComparison.Ordinal)) || editada)) continue;
                // Descripciones cambiadas en la OC de prueba por la F3/F4 («PRUEBA F3 ACTUALIZADA», «… prueba»).
                if (c.ColumnName == "RowDescription" && b.Contains("PRUEBA", StringComparison.OrdinalIgnoreCase)) continue;
                // Precio unitario: el worker lo dejaba a 5 decimales; las 22 primeras conversiones de la F6 son anteriores al redondeo.
                if (c.ColumnName is "UnitCost" or "StockingUnitCost" && IgualA5Decimales(a, b)) continue;
                if (editada && c.ColumnName is "UnitCost" or "StockingUnitCost") continue;
                difs.Add($"JrnlRow[{i}].{c.ColumnName}: worker «{a}» / bridge «{b}»");
            }
        }
        return difs;
    }

    private static bool IgualA5Decimales(string delWorker, string bridge) =>
        decimal.TryParse(delWorker, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
        && decimal.TryParse(bridge, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
        && a == Math.Round(b, 5, MidpointRounding.AwayFromZero);

    private static DataTable Tabla(OdbcConnection cn, string sql)
    {
        var dt = new DataTable();
        using var da = new OdbcDataAdapter(sql, cn);
        da.Fill(dt);
        return dt;
    }

    private static string V(DataRow r, string columna) => T(r[columna]);

    private static string T(object? v) => v is null or DBNull ? string.Empty : Convert.ToString(v, CultureInfo.InvariantCulture)!.Trim();

    private static async Task<TrabajoSage> EsperarAsync(ColaSage cola, long id)
    {
        var limite = DateTime.UtcNow.AddMinutes(15);
        while (true)
        {
            var t = (await cola.ObtenerAsync(id))!;
            if (EstadosTrabajo.EsFinal(t.Estado) || DateTime.UtcNow > limite) return t;
            await Task.Delay(2000);
        }
    }
}
