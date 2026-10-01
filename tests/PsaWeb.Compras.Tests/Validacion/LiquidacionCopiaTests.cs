using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PsaWeb.SageBridge.Cola;
using PsaWeb.SageBridge.Cola.Data;
using PsaWeb.SageBridge.Contratos;
using Xunit.Abstractions;

namespace PsaWeb.Compras.Tests.Validacion;

/// <summary>
/// F0 de Liquidación de Importaciones (§7 y §9 de <c>PLAN-OLA2-LIQUIDACION-IMPORTACIONES.md</c>): <b>ESCRIBE en la copia de prueba</b>.
/// Rehace por el Bridge la OC de una liquidación real (por defecto <c>LIQ IMPORT-041-2026</c>) con la referencia
/// <c>LIQ PRUEBA-…</c>: (a) la crea, (c) la actualiza en el lugar con los mismos datos, (b) la convierte en compra y repite la
/// conversión (idempotencia). Compara todas las columnas de <c>JrnlHdr</c>/<c>JrnlRow</c> de la OC con la del `.exe`, de la compra con
/// la registrada a mano y las capas de <c>InventoryCosts</c>. Deja el informe en <c>%TEMP%\psa-f0-liquidacion.txt</c>.
/// <para>Solo con <c>PSAWEB_TEST_F0_LIQ=1</c>, <c>PSAWEB_TEST_SAGE_COMPRAS</c> y el Bridge en consola.</para>
/// </summary>
public class LiquidacionCopiaTests(ITestOutputHelper salida)
{
    private static readonly HashSet<string> Identidad = new(StringComparer.OrdinalIgnoreCase)
    {
        "PostOrder", "Reference", "INV_POSOOrderNumber", "InvNumForThisTrx", "JrnlKey_TrxNumber", "LastPostedAt", "LastUpdateCounter",
        "rGUIDa", "rGUIDb", "rGUIDc", "rGUIDd", "LinkToAnotherTrx",
    };

    /// <summary>OC: la real ya está recibida y la de prueba se actualizó en el lugar (§9.1).</summary>
    private static readonly HashSet<string> EsperadasOc = new(StringComparer.OrdinalIgnoreCase)
    {
        "POSOisClosed", "LastUsedDistNumber", "AmountReceived", "StockingQtyReceived", "QtyReceived", "DistNumber",
        "GLAcntNumber", // C6: la cuenta por pagar de la OC es 20000 (la del .exe era la de la importación)
    };

    /// <summary>
    /// Compra: diferencias del SDK documentadas en §9.3 (filas secundarias sin cantidad, <c>IncludeInInvLedger</c>, costo de stock con
    /// todos los decimales, LIQUIDACION no enlazada a la OC) y la cuenta por pagar (la manual usa 20000 en 22 de 198).
    /// </summary>
    private static readonly HashSet<string> EsperadasCompra = new(StringComparer.OrdinalIgnoreCase)
    {
        "IncludeInInvLedger", "StockingUnitCost", "Quantity", "UnitCost", "LinkToOtherTrxIndex", "JournalRowEx", "LinkJournalRowEx", "GLAcntNumber",
    };

    private sealed class FabricaCola : IDbContextFactory<SageBridgeDbContext>
    {
        public SageBridgeDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<SageBridgeDbContext>().UseSqlServer(EntornoCompras.PlataformaLocal).Options);
    }

    [SkippableFact]
    public async Task Oc_y_compra_de_liquidacion_por_el_bridge()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("PSAWEB_TEST_F0_LIQ") == "1", "Escribe en la copia de prueba: solo con PSAWEB_TEST_F0_LIQ=1.");
        Skip.If(EntornoCompras.CadenaSage is null, "Sin PSAWEB_TEST_SAGE_COMPRAS.");
        Skip.If(Environment.Is64BitProcess, "El driver ODBC de Pervasive es de 32 bits.");
        var cola = new ColaSage(new FabricaCola());
        Skip.IfNot((await cola.LatidosAsync()).Any(l => EstadoBridge.EstaVivo(l, DateTime.UtcNow)), "El Bridge no está corriendo (PsaWeb.SageBridge.exe --consola).");

        var referenciaReal = Environment.GetEnvironmentVariable("PSAWEB_TEST_F0_LIQ_REF") ?? "LIQ IMPORT-041-2026";
        var referenciaPrueba = "LIQ PRUEBA" + referenciaReal[referenciaReal.IndexOf('-')..];
        await using var cn = new OdbcConnection(EntornoCompras.CadenaSage);
        await cn.OpenAsync();

        var ocReal = Escalar(cn, "SELECT PostOrder FROM JrnlHdr WHERE JrnlKey_Journal = 10 AND JournalEx = 18 AND Reference = ?", referenciaReal)
                     ?? throw new InvalidOperationException($"No existe la OC {referenciaReal} en la copia.");
        var cab = Tabla(cn, $"SELECT h.TransactionDate, v.VendorID, h.CustVendId FROM JrnlHdr h, Vendors v WHERE h.CustVendId = v.VendorRecordNumber AND h.PostOrder = {ocReal}").Rows[0];
        var filas = Tabla(cn, "SELECT r.RowNumber, l.ItemID, r.RowDescription, r.Quantity, r.Amount, c.AccountID, r.ItemRecordNumber " +
                              $"FROM JrnlRow r, LineItem l, Chart c WHERE r.ItemRecordNumber = l.ItemRecordNumber AND r.GLAcntNumber = c.GLAcntNumber AND r.PostOrder = {ocReal} ORDER BY r.RowNumber");
        var cierre = Tabla(cn, $"SELECT c.AccountID FROM JrnlRow r, Chart c WHERE r.GLAcntNumber = c.GLAcntNumber AND r.PostOrder = {ocReal} AND r.ItemRecordNumber = 0 AND r.RowDescription = 'LIQUIDACION'");
        var payload = new PayloadGuardarOcLiquidacion
        {
            CuentaImportacion = T(cierre.Rows[0][0]),
            Fecha = Convert.ToDateTime(cab[0], CultureInfo.InvariantCulture).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ProveedorId = T(cab[1]),
            Referencia = referenciaPrueba,
            CuentaPorPagar = PsaWeb.Compras.Armado.ArmadorOc.CuentaPorPagar,
            Lineas = filas.Rows.Cast<DataRow>().Where(r => Convert.ToInt32(r[6], CultureInfo.InvariantCulture) > 0).Select(r => new LineaOcLiquidacion
            {
                Item = T(r[1]), Descripcion = T(r[2]),
                Cantidad = Convert.ToDecimal(r[3], CultureInfo.InvariantCulture), Monto = Convert.ToDecimal(r[4], CultureInfo.InvariantCulture),
            }).ToList(),
        };

        var informe = new StringBuilder($"F0 liquidación: {referenciaReal} (OC {ocReal}) → {referenciaPrueba}\n");
        var fallas = 0;
        void Anotar(string d, HashSet<string> esperadas)
        {
            var esperada = esperadas.Contains(d.Split(':')[0].Split('.').Last());
            if (!esperada) fallas++;
            informe.AppendLine((esperada ? "   " : " ! ") + d);
        }
        // Se habilita la empresa mientras dura y al final se deja como estaba (antes la dejaba deshabilitada).
        var previa = (await cola.EmpresasAsync()).FirstOrDefault(e => e.Ruc == EntornoCompras.Ruc);
        await cola.GuardarEmpresaAsync(EntornoCompras.Ruc, true, previa?.Ventana, "F0 liquidación (arnés)", "test-f0-liq");
        try
        {
            // (a) crear, o (c) actualizar en el lugar si ya existe de una corrida anterior.
            var existente = Escalar(cn, "SELECT PostOrder FROM JrnlHdr WHERE JrnlKey_Journal = 10 AND JournalEx = 18 AND Reference = ?", referenciaPrueba);
            payload.PostOrder = existente;
            var recibida = existente is { } e && Tabla(cn, $"SELECT COUNT(*) FROM JrnlRow WHERE PostOrder = {e} AND StockingQtyReceived <> 0").Rows[0][0] is var n && Convert.ToInt32(n, CultureInfo.InvariantCulture) > 0;
            int ocPrueba;
            if (recibida)
            {
                // Ya convertida en una corrida anterior: actualizarla debe rechazarse (no se tocan OC contabilizadas).
                var t0 = await CorrerAsync(cola, TiposTrabajo.GuardarOcLiquidacion, payload, informe);
                if (t0.Estado != EstadosTrabajo.Error) fallas++;
                ocPrueba = existente!.Value;
            }
            else
            {
                var t1 = await CorrerAsync(cola, TiposTrabajo.GuardarOcLiquidacion, payload, informe);
                if (t1.Estado != EstadosTrabajo.Hecho) fallas++;
                ocPrueba = JsonSerializer.Deserialize<ResultadoGuardarOcLiquidacion>(t1.ResultadoJson ?? "{}")!.PostOrder;
            }
            if (existente is null && ocPrueba > 0)
            {
                payload.PostOrder = ocPrueba;
                var t2 = await CorrerAsync(cola, TiposTrabajo.GuardarOcLiquidacion, payload, informe);
                if (t2.Estado != EstadosTrabajo.Hecho) fallas++;
            }

            informe.AppendLine($"== OC: .exe {ocReal} / Bridge {ocPrueba}");
            foreach (var d in Comparar(cn, ocReal, ocPrueba)) Anotar(d, EsperadasOc);

            // (b) compra + idempotencia.
            var conv = new PayloadConvertirLiquidacion { PostOrder = ocPrueba };
            var t3 = await CorrerAsync(cola, TiposTrabajo.ConvertirLiquidacion, conv, informe);
            if (t3.Estado != EstadosTrabajo.Hecho) fallas++;
            var t4 = await CorrerAsync(cola, TiposTrabajo.ConvertirLiquidacion, conv, informe);
            if (t4.Estado != EstadosTrabajo.Hecho || !JsonSerializer.Deserialize<ResultadoConvertirLiquidacion>(t4.ResultadoJson ?? "{}")!.YaExistia) fallas++;

            var vendor = Convert.ToInt32(cab[2], CultureInfo.InvariantCulture);
            var compraReal = Escalar(cn, "SELECT PostOrder FROM JrnlHdr WHERE JrnlKey_Journal = 4 AND INV_POSOOrderNumber = ? ORDER BY PostOrder DESC", referenciaReal);
            var compraPrueba = Escalar(cn, "SELECT PostOrder FROM JrnlHdr WHERE JrnlKey_Journal = 4 AND Reference = ? ORDER BY PostOrder DESC", ReferenciasLiquidacion.DeCompra(referenciaPrueba));
            informe.AppendLine($"== Compra: manual {compraReal} / Bridge {compraPrueba}");
            if (compraReal is { } a && compraPrueba is { } b)
            {
                foreach (var d in Comparar(cn, a, b)) Anotar(d, EsperadasCompra);
                informe.AppendLine("== InventoryCosts (manual | Bridge)");
                foreach (var po in new[] { a, b })
                {
                    foreach (DataRow r in Tabla(cn, "SELECT l.ItemID, i.MajorType, i.TransDate, i.Quantity, i.TransAmount, i.OptAmount FROM InventoryCosts i, LineItem l " +
                                                    $"WHERE i.ItemRecNumber = l.ItemRecordNumber AND i.PostOrderNumber = {po} ORDER BY l.ItemID, i.MajorType").Rows)
                    {
                        informe.AppendLine($"   {po} {string.Join(" | ", r.ItemArray.Select(T))}");
                    }
                }
                informe.AppendLine("== Saldo de la cuenta de importación sin la prueba / con la prueba");
                informe.AppendLine("   " + T(Tabla(cn, $"SELECT SUM(r.Amount) FROM JrnlRow r, JrnlHdr h, Chart c WHERE r.PostOrder = h.PostOrder AND r.GLAcntNumber = c.GLAcntNumber AND c.AccountID = '{payload.CuentaImportacion}' AND h.JrnlKey_Journal <> 10 AND h.PostOrder <> {b}").Rows[0][0]) +
                                    " / " + T(Tabla(cn, $"SELECT SUM(r.Amount) FROM JrnlRow r, JrnlHdr h, Chart c WHERE r.PostOrder = h.PostOrder AND r.GLAcntNumber = c.GLAcntNumber AND c.AccountID = '{payload.CuentaImportacion}' AND h.JrnlKey_Journal <> 10").Rows[0][0]));
            }
            else fallas++;
        }
        finally
        {
            await cola.GuardarEmpresaAsync(EntornoCompras.Ruc, previa?.Habilitada ?? false, previa?.Ventana, previa?.Nota ?? "F0 liquidación terminada", "test-f0-liq");
        }

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "psa-f0-liquidacion.txt"), informe.ToString());
        salida.WriteLine(informe.ToString());
        Assert.True(fallas == 0, informe.ToString());
    }

    private static async Task<TrabajoSage> CorrerAsync(ColaSage cola, string tipo, object payload, StringBuilder informe)
    {
        var json = JsonSerializer.Serialize(payload, payload.GetType());
        var id = (await cola.EncolarAsync(EntornoCompras.Ruc, tipo, json, $"test-f0-liq-{tipo}-{DateTime.UtcNow:yyyyMMddHHmmssfff}", "test-f0-liq")).Trabajo.Id;
        var limite = DateTime.UtcNow.AddMinutes(10);
        TrabajoSage t;
        while (true)
        {
            t = (await cola.ObtenerAsync(id))!;
            if (EstadosTrabajo.EsFinal(t.Estado) || DateTime.UtcNow > limite) break;
            await Task.Delay(2000);
        }
        informe.AppendLine($"Trabajo #{id} {tipo}: {t.Estado} {t.Error} {t.ResultadoJson}");
        return t;
    }

    /// <summary>Todas las columnas de <c>JrnlHdr</c> y de cada fila de <c>JrnlRow</c>, salvo las de identidad.</summary>
    private static List<string> Comparar(OdbcConnection cn, int real, int prueba)
    {
        var difs = new List<string>();
        var ha = Tabla(cn, $"SELECT * FROM JrnlHdr WHERE PostOrder = {real}");
        var hb = Tabla(cn, $"SELECT * FROM JrnlHdr WHERE PostOrder = {prueba}");
        foreach (DataColumn c in ha.Columns)
        {
            if (Identidad.Contains(c.ColumnName)) continue;
            var a = T(ha.Rows[0][c.ColumnName]);
            var b = T(hb.Rows[0][c.ColumnName]);
            if (a != b) difs.Add($"JrnlHdr.{c.ColumnName}: real «{a}» / Bridge «{b}»");
        }
        var fa = Tabla(cn, $"SELECT * FROM JrnlRow WHERE PostOrder = {real} ORDER BY RowNumber, RowType, GLAcntNumber");
        var fb = Tabla(cn, $"SELECT * FROM JrnlRow WHERE PostOrder = {prueba} ORDER BY RowNumber, RowType, GLAcntNumber");
        if (fa.Rows.Count != fb.Rows.Count) difs.Add($"Filas: real {fa.Rows.Count} / Bridge {fb.Rows.Count}");
        for (var i = 0; i < Math.Min(fa.Rows.Count, fb.Rows.Count); i++)
        {
            foreach (DataColumn c in fa.Columns)
            {
                if (Identidad.Contains(c.ColumnName)) continue;
                var a = T(fa.Rows[i][c.ColumnName]);
                var b = T(fb.Rows[i][c.ColumnName]);
                if (a != b) difs.Add($"JrnlRow[{i}].{c.ColumnName}: real «{a}» / Bridge «{b}»");
            }
        }
        return difs;
    }

    private static int? Escalar(OdbcConnection cn, string sql, string parametro)
    {
        using var cmd = new OdbcCommand(sql, cn);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = parametro });
        var r = cmd.ExecuteScalar();
        return r is null or DBNull ? null : Convert.ToInt32(r, CultureInfo.InvariantCulture);
    }

    private static DataTable Tabla(OdbcConnection cn, string sql)
    {
        var dt = new DataTable();
        using var da = new OdbcDataAdapter(sql, cn);
        da.Fill(dt);
        return dt;
    }

    private static string T(object? v) => v is null or DBNull ? string.Empty : Convert.ToString(v, CultureInfo.InvariantCulture)!.Trim();
}
