using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace PsaWeb.Conciliacion;

public enum EstadoComprobanteSri { Autorizado, NoAutorizado, FormatoInvalido, NoEncontrado, ErrorServicio }

public sealed record ResultadoVerificacionEstado(EstadoComprobanteSri Estado, string? MensajeSri);

/// <summary>Consulta el estado de un comprobante contra el WS público del SRI, por clave de acceso.</summary>
public interface IVerificadorEstadoSri
{
    Task<ResultadoVerificacionEstado> VerificarAsync(string claveAcceso, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cliente HTTP crudo de <c>ConsultaComprobante.consultarEstadoAutorizacionComprobante</c>
/// (§4.5/§14.1 del plan) — probado en vivo el 2026-09-17 contra
/// <c>cel.sri.gob.ec</c> (producción) con una clave real. Hallazgo que cambia
/// el parseo respecto a lo que dice el WSDL: el campo <c>estadoConsulta</c>
/// **no aparece** cuando el comprobante es válido — solo en los casos de
/// error; el éxito se reconoce por la presencia de <c>estadoAutorizacion</c>.
/// HTTP crudo (no WCF/ServiceReference) — más simple de testear y mantener.
/// </summary>
public sealed class ConsultaComprobanteClient(HttpClient httpClient, IOptions<ConciliacionOptions> opciones)
    : IVerificadorEstadoSri
{
    private const string Namespace = "http://ec.gob.sri.ws.consultas";

    public async Task<ResultadoVerificacionEstado> VerificarAsync(
        string claveAcceso, CancellationToken cancellationToken = default)
    {
        var sobre = $"""
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:tns="{Namespace}">
              <soapenv:Body>
                <tns:consultarEstadoAutorizacionComprobante>
                  <claveAcceso>{claveAcceso}</claveAcceso>
                </tns:consultarEstadoAutorizacionComprobante>
              </soapenv:Body>
            </soapenv:Envelope>
            """;

        string xmlRespuesta;
        try
        {
            using var contenido = new StringContent(sobre, Encoding.UTF8, "text/xml");
            using var respuesta = await httpClient.PostAsync(opciones.Value.UrlConsultaComprobante, contenido, cancellationToken);
            respuesta.EnsureSuccessStatusCode();
            xmlRespuesta = await respuesta.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new ResultadoVerificacionEstado(EstadoComprobanteSri.ErrorServicio, ex.Message);
        }

        return InterpretarRespuesta(xmlRespuesta);
    }

    internal static ResultadoVerificacionEstado InterpretarRespuesta(string xmlRespuesta)
    {
        XElement estado;
        try
        {
            var doc = XDocument.Parse(xmlRespuesta);
            estado = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "EstadoAutorizacionComprobante")
                ?? throw new InvalidOperationException("Respuesta sin EstadoAutorizacionComprobante.");
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
        {
            return new ResultadoVerificacionEstado(EstadoComprobanteSri.ErrorServicio, "Respuesta del SRI no reconocida.");
        }

        var estadoAutorizacion = Texto(estado, "estadoAutorizacion");
        if (!string.IsNullOrEmpty(estadoAutorizacion))
        {
            return string.Equals(estadoAutorizacion, "AUTORIZADO", StringComparison.OrdinalIgnoreCase)
                ? new ResultadoVerificacionEstado(EstadoComprobanteSri.Autorizado, estadoAutorizacion)
                : new ResultadoVerificacionEstado(EstadoComprobanteSri.NoAutorizado, estadoAutorizacion);
        }

        // Sin estadoAutorizacion: caso de error — estadoConsulta="RECHAZADA" +
        // el motivo real en informacionAdicional del primer mensaje.
        var informacionAdicional = estado.Descendants().FirstOrDefault(e => e.Name.LocalName == "informacionAdicional")?.Value ?? string.Empty;

        if (informacionAdicional.Contains("formato", StringComparison.OrdinalIgnoreCase))
        {
            return new ResultadoVerificacionEstado(EstadoComprobanteSri.FormatoInvalido, informacionAdicional);
        }

        if (informacionAdicional.Contains("no existen", StringComparison.OrdinalIgnoreCase))
        {
            return new ResultadoVerificacionEstado(EstadoComprobanteSri.NoEncontrado, informacionAdicional);
        }

        return new ResultadoVerificacionEstado(EstadoComprobanteSri.ErrorServicio,
            informacionAdicional.Length > 0 ? informacionAdicional : "El SRI no devolvió un estado reconocible.");
    }

    private static string Texto(XElement padre, string nombreLocal) =>
        padre.Descendants().FirstOrDefault(e => e.Name.LocalName == nombreLocal)?.Value ?? string.Empty;
}
