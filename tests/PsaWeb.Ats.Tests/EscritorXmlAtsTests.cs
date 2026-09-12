using System.Xml.Linq;
using PsaWeb.Ats.Esquema;

namespace PsaWeb.Ats.Tests;

public class EscritorXmlAtsTests
{
    private static ivaType AtsMinimo() => new()
    {
        IdInformante = "1792051800001",
        razonSocial = "CPTDC CHINA PETROLEUM TECHNOLOGY y DEVELOPMENT CORPORATION ECUADOR S A",
        Anio = "2026",
        Mes = "07",
        numEstabRuc = "001",
        totalVentas = 7380817.15m,
        totalVentasSpecified = true,
    };

    [Fact]
    public void Serializa_con_raiz_iva_y_sin_namespaces()
    {
        var xml = System.Text.Encoding.UTF8.GetString(EscritorXmlAts.Serializar(AtsMinimo()));

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"no\"?><iva>", xml);
        Assert.DoesNotContain("xmlns", xml);
        Assert.DoesNotContain("\r\n", xml); // sin indentar, como el .exe y el XML real del SRI
        Assert.Contains("<IdInformante>1792051800001</IdInformante>", xml);
        Assert.Contains("<numEstabRuc>001</numEstabRuc>", xml);
    }

    [Fact]
    public void No_emite_BOM()
    {
        var bytes = EscritorXmlAts.Serializar(AtsMinimo());

        // El BOM UTF-8 son los 3 bytes EF BB BF al inicio.
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact]
    public void El_XML_generado_es_parseable_y_conserva_los_valores()
    {
        var ats = AtsMinimo();
        var xml = System.Text.Encoding.UTF8.GetString(EscritorXmlAts.Serializar(ats));

        var doc = XDocument.Parse(xml);

        Assert.Equal("iva", doc.Root!.Name.LocalName);
        Assert.Equal(ats.razonSocial, doc.Root.Element("razonSocial")?.Value);
        Assert.Equal("7380817.15", doc.Root.Element("totalVentas")?.Value);
    }

    [Fact]
    public void Round_trip_deserializa_igual_a_lo_que_se_armo()
    {
        var original = AtsMinimo();
        var xml = EscritorXmlAts.Serializar(original);

        using var stream = new MemoryStream(xml);
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(ivaType));
        var vueltaAObjeto = (ivaType)serializer.Deserialize(stream)!;

        Assert.Equal(original.IdInformante, vueltaAObjeto.IdInformante);
        Assert.Equal(original.razonSocial, vueltaAObjeto.razonSocial);
        Assert.Equal(original.Anio, vueltaAObjeto.Anio);
        Assert.Equal(original.Mes, vueltaAObjeto.Mes);
        Assert.Equal(original.numEstabRuc, vueltaAObjeto.numEstabRuc);
        Assert.Equal(original.totalVentas, vueltaAObjeto.totalVentas);
    }
}
