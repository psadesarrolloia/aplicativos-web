using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PsaWeb.Compras.Importaciones;
using Xunit.Abstractions;

namespace PsaWeb.Compras.Tests.Validacion;

/// <summary>
/// Cierre de la F1 de Liquidación de Importaciones (§7 de <c>PLAN-OLA2-LIQUIDACION-IMPORTACIONES.md</c>), solo lectura: para cada
/// liquidación guardada en PeachEBills con OC (la última de cada cuenta), recalcula el prorrateo con <see cref="Liquidaciones.Prorratear"/>
/// desde lo guardado y lo compara con lo guardado; después arma la OC con <see cref="ArmadorOcLiquidacion"/> y la compara con la OC real
/// de Sage (proveedor, referencia, fecha, cuenta, y por línea ítem, descripción, cantidad, monto y precio; más la línea LIQUIDACION).
/// Informe en <c>%TEMP%\psa-f1-liquidaciones.txt</c>.
/// </summary>
public class ReconstruccionLiquidacionesTests(ITestOutputHelper salida)
{
    /// <summary>
    /// OC que contabilidad corrigió en Sage después de crearlas (revisado 2026-09-29): cantidades pasadas a metros con el mismo monto
    /// (CA-*), ítems cambiados, un monto cambiado, o la línea LIQUIDACION contra otra cuenta (cuentas renombradas o divididas).
    /// No son diferencias del armado: se informan y no hacen fallar.
    /// </summary>
    private static readonly Dictionary<int, string> OcEditadasEnSage = new()
    {
        [52104] = "LIQUIDACION a 13647-A", [52109] = "LIQUIDACION a 13647-B en 0", [58303] = "ítem CA-003 → CA-003-2",
        [61664] = "ítems GD-761/GD-088 cambiados", [72046] = "cuenta 13710-8-339 renombrada a 13710", [72050] = "cuenta 13711-8-339 renombrada a 13711",
        [82153] = "cantidades a metros", [82161] = "cantidad a metros", [84803] = "cantidad a metros", [88923] = "cantidad a metros",
        [90486] = "cantidad a metros", [92250] = "cantidad a metros", [92287] = "cantidades a metros", [92526] = "cantidad a metros",
        [103810] = "monto 88.822,84 → 85.822,84",
    };

    [SkippableFact]
    public async Task Prorrateo_y_OC_de_las_liquidaciones_reales()
    {
        Skip.If(EntornoCompras.CadenaSage is null, "Sin PSAWEB_TEST_SAGE_COMPRAS (cadena ODBC de la copia de prueba).");
        Skip.If(Environment.Is64BitProcess, "El driver ODBC de Pervasive es de 32 bits.");

        await using var db = EntornoCompras.PeachEbills();
        var cuentas = await db.ImportCost.AsNoTracking().Where(x => x.TransmitterRuc == EntornoCompras.Ruc)
            .Select(x => x.AccountId).Distinct().ToListAsync();
        await using var cn = new OdbcConnection(EntornoCompras.CadenaSage);
        await cn.OpenAsync();
        var proveedores = (await LectorImportaciones.ProveedoresAsync(cn)).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

        var informe = new StringBuilder();
        int conOc = 0, sinOc = 0, ocBorrada = 0, prorrateoOk = 0, ocOk = 0, editadas = 0;
        var fallas = new List<string>();
        foreach (var cuenta in cuentas.OrderBy(x => x))
        {
            var g = await RepositorioLiquidaciones.UltimaAsync(db, EntornoCompras.Ruc, cuenta);
            if (g is null) continue;
            if (g.PostOrder is not { } po)
            {
                sinOc++;
                continue;
            }
            conOc++;

            // 1) Prorrateo desde lo guardado.
            var items = g.Items.Select(x => x.Copia()).ToList();
            var (gastos, factura) = Liquidaciones.Totales(g.Gastos);
            var error = Liquidaciones.Prorratear(items, factura, gastos);
            var difsP = new List<string>();
            if (error is not null) difsP.Add(error);
            else
            {
                for (var i = 0; i < items.Count; i++)
                {
                    // El % difiere a veces en 1 ulp: Math.Round(double, 6) de .NET Framework no es exacto (B5, solo pantalla).
                    if (Math.Abs(items[i].Porcentaje - g.Items[i].Porcentaje) > 1e-12 || items[i].Prorrateo != g.Items[i].Prorrateo)
                    {
                        difsP.Add($"{items[i].ItemId}: % {g.Items[i].Porcentaje} → {items[i].Porcentaje}, prorrateo {g.Items[i].Prorrateo} → {items[i].Prorrateo}");
                    }
                }
            }
            if (difsP.Count == 0) prorrateoOk++;
            else fallas.Add($"{cuenta} prorrateo: {string.Join(" | ", difsP)}");

            // 2) OC armada desde lo guardado vs la OC real.
            var oc = await LectorImportaciones.OcAsync(cn, po);
            if (oc is null)
            {
                ocBorrada++;
                informe.AppendLine($"{cuenta}: la OC {po} ({g.ReferenciaOc}) ya no está en Sage.");
                continue;
            }
            var (payload, errores) = ArmadorOcLiquidacion.Armar(new EntradaOcLiquidacion(
                // Como el `.exe` al abrir: con la OC en Sage, proveedor, referencia y fecha salen de ella.
                cuenta, oc.ProveedorId, oc.Fecha, oc.Referencia, po, g.Items, g.Gastos), proveedores);
            var difs = new List<string>();
            if (payload is null) difs.Add("no arma: " + string.Join(" | ", errores));
            else
            {
                if (payload.ProveedorId != oc.ProveedorId) difs.Add($"proveedor {oc.ProveedorId} / {payload.ProveedorId}");
                if (payload.Referencia != oc.Referencia) difs.Add($"referencia «{oc.Referencia}» / «{payload.Referencia}»");
                if (payload.Fecha != oc.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) difs.Add($"fecha {oc.Fecha:yyyy-MM-dd} / {payload.Fecha}");
                var filas = Tabla(cn, "SELECT r.RowNumber, l.ItemID, r.RowDescription, r.Quantity, r.Amount, r.UnitCost, c.AccountID, r.ItemRecordNumber " +
                                      "FROM JrnlRow r, LineItem l, Chart c WHERE r.ItemRecordNumber = l.ItemRecordNumber AND r.GLAcntNumber = c.GLAcntNumber " +
                                      $"AND r.PostOrder = {po} ORDER BY r.RowNumber");
                var cabecera = filas.Rows.Cast<DataRow>().FirstOrDefault(r => Convert.ToInt32(r[0], CultureInfo.InvariantCulture) == 0);
                if (cabecera is not null && T(cabecera[6]) != cuenta) difs.Add($"cuenta AP {T(cabecera[6])} / {cuenta}");
                var lineas = filas.Rows.Cast<DataRow>().Where(r => Convert.ToInt32(r[0], CultureInfo.InvariantCulture) > 0).ToList();
                var itemsReales = lineas.Where(r => Convert.ToInt32(r[7], CultureInfo.InvariantCulture) > 0).ToList();
                if (itemsReales.Count != payload.Lineas.Count) difs.Add($"líneas de ítem {itemsReales.Count} / {payload.Lineas.Count}");
                for (var i = 0; i < Math.Min(itemsReales.Count, payload.Lineas.Count); i++)
                {
                    var r = itemsReales[i];
                    var l = payload.Lineas[i];
                    var monto = Math.Round(l.Monto, 2, MidpointRounding.AwayFromZero);
                    var montoReal = Convert.ToDecimal(r[4], CultureInfo.InvariantCulture);
                    var cantidad = Convert.ToDecimal(r[3], CultureInfo.InvariantCulture);
                    if (T(r[1]) != l.Item) difs.Add($"[{i}] ítem {T(r[1])} / {l.Item}");
                    if (T(r[2]) != l.Descripcion.Trim()) difs.Add($"[{i}] descripción «{T(r[2])}» / «{l.Descripcion}»");
                    if (cantidad != l.Cantidad) difs.Add($"[{i}] cantidad {cantidad} / {l.Cantidad}");
                    if (montoReal != monto) difs.Add($"[{i}] monto {montoReal} / {monto} ({l.Monto})");
                    var pu = Convert.ToDecimal(r[5], CultureInfo.InvariantCulture);
                    if (cantidad != 0 && Math.Abs(pu - montoReal / cantidad) > 0.0000001m) difs.Add($"[{i}] precio {pu} / {montoReal / cantidad}");
                }
                var liq = Tabla(cn, "SELECT r.RowNumber, '', r.RowDescription, r.Quantity, r.Amount, r.UnitCost, c.AccountID, r.ItemRecordNumber FROM JrnlRow r, Chart c " +
                                   $"WHERE r.GLAcntNumber = c.GLAcntNumber AND r.PostOrder = {po} AND r.ItemRecordNumber = 0 AND r.RowNumber > 0").Rows.Cast<DataRow>().ToList();
                var totalOc = payload.Lineas.Sum(x => Math.Round(x.Monto, 2, MidpointRounding.AwayFromZero));
                if (liq.Count != 1 || T(liq[0][2]) != "LIQUIDACION" || Convert.ToDecimal(liq[0][4], CultureInfo.InvariantCulture) != -totalOc || T(liq[0][6]) != cuenta)
                {
                    difs.Add("línea LIQUIDACION: " + string.Join(" ; ", liq.Select(r => $"{T(r[2])} {T(r[4])} {T(r[6])}")) + $" / −{totalOc} {cuenta}");
                }
            }
            if (difs.Count == 0) ocOk++;
            else if (OcEditadasEnSage.TryGetValue(po, out var motivo))
            {
                editadas++;
                informe.AppendLine($"{cuenta} OC {po} {oc.Referencia} (editada en Sage: {motivo}): {string.Join(" | ", difs)}");
            }
            else fallas.Add($"{cuenta} OC {po} {oc.Referencia}: {string.Join(" | ", difs)}");
        }

        informe.Insert(0, $"Liquidaciones CPTDC: {cuentas.Count} cuentas; con OC {conOc} (OC borrada en Sage {ocBorrada}), sin OC {sinOc}.\n" +
                          $"Prorrateo idéntico: {prorrateoOk}/{conOc}. OC idéntica: {ocOk}/{conOc - ocBorrada}.\n");
        foreach (var f in fallas) informe.AppendLine(f);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "psa-f1-liquidaciones.txt"), informe.ToString());
        salida.WriteLine(informe.ToString());
        Assert.True(fallas.Count == 0, informe.ToString());
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
