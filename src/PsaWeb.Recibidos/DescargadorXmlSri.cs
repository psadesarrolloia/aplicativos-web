using System.Text;
using System.Xml.Linq;

namespace PsaWeb.Recibidos;

/// <summary>Resultado de pedir un XML al WS de autorización.</summary>
/// <param name="Contenido">Respuesta SOAP completa (se guarda tal cual) si trae el comprobante; <c>null</c> si no.</param>
/// <param name="SinComprobante">El WS respondió bien pero sin comprobante: fuera de su ventana (~15 días) o clave inexistente.</param>
public sealed record ResultadoDescargaXml(string? Contenido, bool SinComprobante, string? Error);

public interface IDescargadorXmlSri
{
    Task<ResultadoDescargaXml> DescargarAsync(string claveAcceso, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cliente HTTP crudo de <c>AutorizacionComprobantesOffline.autorizacionComprobante</c> (producción, §1 y §4.4 del plan). El
/// WS entrega el XML solo ~15 días después de la emisión (medido en la F0/F2: 147 de 147 dentro de la ventana). Tres intentos
/// ante fallas transitorias de TLS/red, como <c>ConsultaComprobanteClient</c>.
/// </summary>
public sealed class DescargadorXmlSri(HttpClient http) : IDescargadorXmlSri
{
    public const string Url = "https://cel.sri.gob.ec/comprobantes-electronicos-ws/AutorizacionComprobantesOffline";

    public async Task<ResultadoDescargaXml> DescargarAsync(string claveAcceso, CancellationToken cancellationToken = default)
    {
        if (claveAcceso.Length != 49 || !claveAcceso.All(char.IsDigit))
        {
            return new(null, false, "La clave de acceso debe tener 49 dígitos.");
        }
        var sobre = "<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:ec=\"http://ec.gob.sri.ws.autorizacion\">" +
                    "<soapenv:Header/><soapenv:Body><ec:autorizacionComprobante>" +
                    $"<claveAccesoComprobante>{claveAcceso}</claveAccesoComprobante>" +
                    "</ec:autorizacionComprobante></soapenv:Body></soapenv:Envelope>";
        var ultimoError = string.Empty;
        for (var intento = 1; intento <= 3; intento++)
        {
            try
            {
                using var contenido = new StringContent(sobre, Encoding.UTF8, "text/xml");
                using var respuesta = await http.PostAsync(Url, contenido, cancellationToken);
                var texto = await respuesta.Content.ReadAsStringAsync(cancellationToken);
                if (!respuesta.IsSuccessStatusCode)
                {
                    ultimoError = $"HTTP {(int)respuesta.StatusCode}";
                }
                else
                {
                    return Interpretar(texto);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                ultimoError = ex.Message;
            }
            if (intento < 3) await Task.Delay(TimeSpan.FromMilliseconds(800 * intento), cancellationToken);
        }
        return new(null, false, ultimoError);
    }

    /// <summary>La respuesta trae <c>numeroComprobantes</c>: 1 = con el XML; 0 = sin comprobante.</summary>
    internal static ResultadoDescargaXml Interpretar(string respuesta)
    {
        try
        {
            var doc = XDocument.Parse(respuesta);
            var numero = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "numeroComprobantes")?.Value.Trim();
            var comprobante = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "comprobante");
            var estado = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "estado")?.Value.Trim();
            if (numero is null or "0" || comprobante is null)
            {
                return new(null, true, null);
            }
            if (estado is not null && estado != "AUTORIZADO")
            {
                return new(null, false, $"El SRI devolvió el comprobante con estado {estado}.");
            }
            return new(respuesta, false, null);
        }
        catch (System.Xml.XmlException ex)
        {
            return new(null, false, "Respuesta del SRI ilegible: " + ex.Message);
        }
    }
}
