using System.Text;
using System.Xml;
using System.Xml.Serialization;
using PsaWeb.Ats.Esquema;

namespace PsaWeb.Ats;

/// <summary>
/// Serializa un <see cref="ivaType"/> (el ATS ya armado en memoria) al XML que
/// se sube al DIMM del SRI. Port de <c>ATSform.SaveXmlFile</c>
/// (<c>ATSfromPeach</c>): <see cref="XmlSerializer"/> directo sobre el
/// esquema, sin plantilla ni concatenación de strings.
/// </summary>
public static class EscritorXmlAts
{
    private static readonly XmlSerializer Serializador = new(typeof(ivaType));

    /// <summary>
    /// Namespaces vacíos: el ATS no declara <c>xmlns:xsi</c>/<c>xmlns:xsd</c>
    /// (el `.exe` tampoco los emite).
    /// </summary>
    private static readonly XmlSerializerNamespaces SinNamespaces = Crear();

    private static XmlSerializerNamespaces Crear()
    {
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add(string.Empty, string.Empty);
        return namespaces;
    }

    /// <summary>
    /// Serializa <paramref name="ats"/> a los bytes del archivo
    /// <c>ATS_{ruc}_{aaaamm}.xml</c>. Reproduce el formato real observado en
    /// declaraciones del SRI (sin indentar, UTF-8 sin BOM,
    /// <c>standalone="no"</c>) — no persigue un diff de bytes exacto contra el
    /// `.exe` (ver docs/PLAN-APP3-ATS.md §3.1): el objetivo es que el XML
    /// cargue igual en el DIMM real.
    /// </summary>
    public static byte[] Serializar(ivaType ats)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
        };

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            writer.WriteStartDocument(standalone: false);
            Serializador.Serialize(writer, ats, SinNamespaces);
        }

        return stream.ToArray();
    }
}
