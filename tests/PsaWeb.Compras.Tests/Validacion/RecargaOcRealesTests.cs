using System.Data.Odbc;
using System.Globalization;
using System.Text;
using PsaWeb.Compras.Armado;
using PsaWeb.Compras.Catalogo;
using PsaWeb.Compras.Sage;
using Xunit.Abstractions;

namespace PsaWeb.Compras.Tests.Validacion;

/// <summary>
/// F4 — recarga de una OC guardada (<see cref="RecargaOc"/>, corrección C9): cada OC real de la copia se lee de Sage, se
/// reconstruye el formulario SIN el XML (como al abrir <c>/compras/{postOrder}</c>) y se vuelve a armar: debe dar las mismas
/// filas que tiene Sage (guardar sin tocar nada no cambia la OC). SOLO LECTURA. Variables en <see cref="EntornoCompras"/>.
/// </summary>
public class RecargaOcRealesTests(ITestOutputHelper salida)
{
    [SkippableFact]
    public async Task Reabrir_y_volver_a_armar_una_oc_guardada_da_la_misma_oc()
    {
        Skip.If(EntornoCompras.CadenaSage is null, "Sin PSAWEB_TEST_SAGE_COMPRAS (cadena ODBC de la copia de prueba).");
        Skip.If(Environment.Is64BitProcess, "El driver ODBC de Pervasive es de 32 bits.");
        await using var pe = EntornoCompras.PeachEbills();
        Skip.IfNot(await pe.Database.CanConnectAsync(), "PeachEBills local no disponible.");

        await using var cn = new OdbcConnection(EntornoCompras.CadenaSage);
        await cn.OpenAsync();
        var catalogo = await LectorCatalogoCompras.LeerAsync(cn, await LectorCatalogoPeachEbills.LeerAsync(pe));

        var informe = new StringBuilder();
        int comparadas = 0, conDiferencias = 0, conAvisos = 0, advertidas = 0;
        foreach (var (cab, _) in await ReconstructorOc.LeerOcsAsync(cn))
        {
            var oc = (await LectorOcs.LeerAsync(cn, cab.PostOrder))!;
            var proveedor = (await LectorCatalogoCompras.ProveedorPorIdAsync(cn, oc.Cabecera.VendorId))!;
            comparadas++;
            var r = RecargaOc.Reconstruir(oc, proveedor, catalogo, EntornoCompras.Ruc);
            if (r.Avisos.Count > 0) conAvisos++;
            var difs = r.Avisos.Select(a => "Aviso: " + a).ToList();
            var armado = ArmadorOc.Armar(r.Entrada, catalogo);
            if (armado.Oc is not { } nueva) difs.AddRange(armado.Errores.Select(e => "Validación: " + e));
            else difs.AddRange(Comparar(nueva, proveedor, oc));
            if (difs.Count == 0) continue;
            if (r.Avisos.Any(a => a.StartsWith("Las retenciones recalculadas no coinciden")))
            {
                // La OC en Sage no cuadra (p. ej. OC-8902, monto cambiado a mano): la recarga lo advierte, que es lo esperado.
                advertidas++;
                informe.AppendLine($"== {cab.Referencia}: advertida al reabrir (no cuadra en Sage)");
                continue;
            }
            conDiferencias++;
            informe.AppendLine($"== {cab.Referencia} (PostOrder {cab.PostOrder}, {cab.VendorId})");
            foreach (var d in difs) informe.AppendLine("   " + d);
        }

        var resumen = $"OC reabiertas: {comparadas} · con diferencias: {conDiferencias} · con avisos: {conAvisos} · advertidas (no cuadran en Sage): {advertidas}";
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "psa-f4-recarga.txt"), resumen + Environment.NewLine + informe);
        salida.WriteLine(resumen);
        salida.WriteLine(informe.ToString());
        Assert.True(comparadas > 0);
        Assert.True(conDiferencias == 0, resumen + Environment.NewLine + informe);
    }

    private static List<string> Comparar(OcArmada nueva, ProveedorSage proveedor, OcGuardada oc)
    {
        var difs = new List<string>();
        if (nueva.NumeroRetencion != (oc.Cabecera.Retencion.Length == 0 ? null : oc.Cabecera.Retencion))
            difs.Add($"Retención: web «{nueva.NumeroRetencion}» / Sage «{oc.Cabecera.Retencion}»");
        if (nueva.Lineas.Count != oc.Filas.Count) difs.Add($"Líneas: web {nueva.Lineas.Count} / Sage {oc.Filas.Count}");
        for (var i = 0; i < Math.Min(nueva.Lineas.Count, oc.Filas.Count); i++)
        {
            var w = nueva.Lineas[i];
            var s = oc.Filas[i];
            var cuenta = w.CuentaId ?? proveedor.CuentaGasto;
            if ((w.ItemId ?? "") != s.ItemId || w.Descripcion.Trim() != s.Descripcion || w.Cantidad != s.Cantidad || w.Monto != s.Monto
                || Math.Round(w.PrecioUnitario, 15) != Math.Round(s.PrecioUnitario, 15) || cuenta != s.Cuenta || (w.JobId ?? "") != s.Job)
            {
                difs.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Fila {s.Numero} ({w.Tipo}): web [{w.ItemId}|{w.Descripcion}|{w.Cantidad}|{w.Monto}|{cuenta}] / Sage [{s.ItemId}|{s.Descripcion}|{s.Cantidad}|{s.Monto}|{s.Cuenta}]"));
            }
        }
        return difs;
    }
}
