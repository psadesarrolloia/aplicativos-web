using System.Data.Odbc;
using System.Text;
using PsaWeb.Compras.Armado;
using PsaWeb.Compras.Catalogo;
using Xunit.Abstractions;

namespace PsaWeb.Compras.Tests.Validacion;

/// <summary>
/// Cierre de la F2 (§10 y §16 del plan de la Ola 2): reconstruye, desde el XML del SRI, las OC que el `.exe` registró en la
/// copia de prueba y las compara fila a fila con Sage. SOLO LECTURA (ODBC SELECT + PeachEBills local). Lo que se valida es
/// todo lo que calcula la lógica: ítem C, IVA, bases y montos de retención, precios, orden de las líneas, AUT-SRI, cabecera.
/// Variables en <see cref="EntornoCompras"/>.
/// </summary>
public class ReconstruccionOcRealesTests(ITestOutputHelper salida)
{
    [SkippableFact]
    public async Task Las_oc_reales_se_reconstruyen_desde_su_xml_sin_diferencias()
    {
        Skip.If(EntornoCompras.CadenaSage is null, "Sin PSAWEB_TEST_SAGE_COMPRAS (cadena ODBC de la copia de prueba).");
        Skip.IfNot(Directory.Exists(EntornoCompras.CarpetaXml), $"No existe {EntornoCompras.CarpetaXml}.");
        Skip.If(Environment.Is64BitProcess, "El driver ODBC de Pervasive es de 32 bits.");

        await using var pe = EntornoCompras.PeachEbills();
        Skip.IfNot(await pe.Database.CanConnectAsync(), "PeachEBills local no disponible.");

        await using var cn = new OdbcConnection(EntornoCompras.CadenaSage);
        await cn.OpenAsync();
        var catalogo = await LectorCatalogoCompras.LeerAsync(cn, await LectorCatalogoPeachEbills.LeerAsync(pe));

        var informe = new StringBuilder();
        var montosEditados = new List<string>();
        int comparadas = 0, conDiferencias = 0, sinXml = 0, resumidas = 0, editadas = 0;
        foreach (var (cab, filas) in await ReconstructorOc.LeerOcsAsync(cn))
        {
            var ruta = Path.Combine(EntornoCompras.CarpetaXml, filas.First(f => f.ItemId == "AUT-SRI").Descripcion + ".xml");
            if (!File.Exists(ruta)) { sinXml++; continue; }
            comparadas++;
            var r = await ReconstructorOc.ReconstruirAsync(cn, pe, catalogo, cab, filas, File.ReadAllText(ruta));
            if (r.MontoEditado is not null) { montosEditados.Add(r.MontoEditado); continue; }
            if (r.Resumida) resumidas++;
            editadas += r.DescripcionesEditadas;
            var difs = r.Errores.ToList();
            if (r.Entrada is not null)
            {
                var armado = ArmadorOc.Armar(r.Entrada, catalogo);
                if (armado.Oc is { } oc) difs.AddRange(ReconstructorOc.Comparar(oc, r.Proveedor!, cab, filas));
                else difs.AddRange(armado.Errores.Select(e => "Validación: " + e));
            }
            if (difs.Count == 0) continue;
            conDiferencias++;
            informe.AppendLine($"== {cab.Referencia} (PostOrder {cab.PostOrder}, {cab.VendorId})");
            foreach (var d in difs) informe.AppendLine("   " + d);
        }

        var resumen = $"OC comparadas: {comparadas} · con diferencias: {conDiferencias} · sin XML: {sinXml} · " +
                      $"resumidas: {resumidas} · descripciones editadas por el digitador: {editadas} · " +
                      $"con monto cambiado a mano (no cuadran con la factura): {montosEditados.Count}";
        foreach (var m in montosEditados) informe.AppendLine("   Monto editado — " + m);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "psa-f2-comparacion.txt"), resumen + Environment.NewLine + informe);
        salida.WriteLine(resumen);
        salida.WriteLine(informe.ToString());
        Assert.True(comparadas > 0, "No se encontró ninguna OC con su XML.");
        Assert.True(conDiferencias == 0, resumen + Environment.NewLine + informe);
    }
}
