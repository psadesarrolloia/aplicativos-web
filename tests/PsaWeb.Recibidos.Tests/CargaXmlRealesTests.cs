using PsaWeb.Recibidos.Data;

namespace PsaWeb.Recibidos.Tests;

/// <summary>
/// Carga en el almacén LOCAL (<c>PsaWebPlataforma</c> de desarrollo) los XML reales bajados del WS en la F2
/// (<c>C:\PSA-F2\xml</c>, fuera del repo): valida el almacén con comprobantes reales y deja la bandeja de recibidos lista para
/// probar. Solo corre con <c>PSAWEB_TEST_CARGAR_XML=1</c>. Idempotente.
/// </summary>
[Collection("PlataformaRecibidos")]
public class CargaXmlRealesTests
{
    private static readonly string Carpeta = Environment.GetEnvironmentVariable("PSAWEB_TEST_XML_COMPRAS") ?? @"C:\PSA-F2\xml";
    private static readonly string Ruc = Environment.GetEnvironmentVariable("PSAWEB_TEST_RUC_COMPRAS") ?? "1792051800001";

    [SkippableFact]
    public async Task Los_xml_reales_se_validan_y_guardan()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("PSAWEB_TEST_CARGAR_XML") == "1", "Solo con PSAWEB_TEST_CARGAR_XML=1 (escribe en la base local).");
        Skip.IfNot(Directory.Exists(Carpeta), $"No existe {Carpeta}.");
        Skip.IfNot(AlmacenXmlRecibidosTests.Disponible(), "PsaWebPlataforma local no disponible.");
        var almacen = new AlmacenXmlRecibidos(new AlmacenXmlRecibidosTests.Fabrica());

        var resultados = new Dictionary<ResultadoGuardadoXml, int>();
        var rechazos = new List<string>();
        foreach (var archivo in Directory.GetFiles(Carpeta, "*.xml"))
        {
            var clave = Path.GetFileNameWithoutExtension(archivo);
            var g = await almacen.GuardarAsync(Ruc, File.ReadAllText(archivo), OrigenesXml.WsSri, "carga-f5", clave);
            resultados[g.Resultado] = resultados.GetValueOrDefault(g.Resultado) + 1;
            if (g.Resultado == ResultadoGuardadoXml.Rechazado) rechazos.Add($"{clave}: {g.Motivo}");
        }

        Assert.True(resultados.Values.Sum() > 0);
        Assert.True(rechazos.Count == 0, string.Join(Environment.NewLine, rechazos));
        Assert.False(resultados.ContainsKey(ResultadoGuardadoXml.Conflicto));
    }
}
