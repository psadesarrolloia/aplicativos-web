using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace PsaWeb.Conciliacion;

/// <summary>
/// Estado de un comprobante según el SRI (o Datil). <c>Anulado</c> es el `ANULADO` real que devuelve el
/// WS; <c>FueraDeRango</c> = el WS solo responde por el mes en curso y el anterior; <c>Otro</c> = un
/// valor que no conocemos (se guarda la respuesta cruda para no perderlo).
/// </summary>
public enum EstadoComprobanteSri
{
    Autorizado, NoAutorizado, Anulado, FormatoInvalido, NoEncontrado, FueraDeRango, Otro, ErrorServicio,
}

public sealed record ResultadoVerificacionEstado(
    EstadoComprobanteSri Estado, string? MensajeSri, string? RespuestaCruda = null);

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

        // Reintentos: en la prueba masiva del 2026-09-19 ~0,8 % de las llamadas fallaron de forma
        // transitoria (error TLS / red) y pasaron al reintentar.
        const int Intentos = 3;
        string? xmlRespuesta = null;
        string ultimoError = string.Empty;
        for (var intento = 1; intento <= Intentos; intento++)
        {
            try
            {
                using var contenido = new StringContent(sobre, Encoding.UTF8, "text/xml");
                using var respuesta = await httpClient.PostAsync(opciones.Value.UrlConsultaComprobante, contenido, cancellationToken);
                respuesta.EnsureSuccessStatusCode();
                xmlRespuesta = await respuesta.Content.ReadAsStringAsync(cancellationToken);
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                ultimoError = ex.InnerException?.Message ?? ex.Message;
                if (intento < Intentos)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(400 * intento), cancellationToken);
                }
            }
        }

        return xmlRespuesta is null
            ? new ResultadoVerificacionEstado(EstadoComprobanteSri.ErrorServicio, ultimoError)
            : InterpretarRespuesta(xmlRespuesta);
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
            // Valores observados en producción (2026-09-19, 1.453 comprobantes): AUTORIZADO y ANULADO.
            // Cualquier otro valor NO se asume: queda como «Otro» con la respuesta cruda para revisarlo.
            var normal = estadoAutorizacion.Trim().ToUpperInvariant();
            var clasificado = normal switch
            {
                "AUTORIZADO" => EstadoComprobanteSri.Autorizado,
                "ANULADO" => EstadoComprobanteSri.Anulado,
                "NO AUTORIZADO" or "RECHAZADA" or "DEVUELTA" => EstadoComprobanteSri.NoAutorizado,
                _ => EstadoComprobanteSri.Otro,
            };
            return new ResultadoVerificacionEstado(clasificado, estadoAutorizacion, xmlRespuesta);
        }

        // Sin estadoAutorizacion: caso de error — estadoConsulta="RECHAZADA" +
        // el motivo real en informacionAdicional del primer mensaje.
        var informacionAdicional = estado.Descendants().FirstOrDefault(e => e.Name.LocalName == "informacionAdicional")?.Value ?? string.Empty;

        // El WS solo responde por comprobantes del mes en curso y el mes anterior.
        if (informacionAdicional.Contains("fuera del rango", StringComparison.OrdinalIgnoreCase))
        {
            return new ResultadoVerificacionEstado(EstadoComprobanteSri.FueraDeRango, informacionAdicional, xmlRespuesta);
        }

        if (informacionAdicional.Contains("formato", StringComparison.OrdinalIgnoreCase))
        {
            return new ResultadoVerificacionEstado(EstadoComprobanteSri.FormatoInvalido, informacionAdicional, xmlRespuesta);
        }

        if (informacionAdicional.Contains("no existen", StringComparison.OrdinalIgnoreCase))
        {
            return new ResultadoVerificacionEstado(EstadoComprobanteSri.NoEncontrado, informacionAdicional, xmlRespuesta);
        }

        return new ResultadoVerificacionEstado(EstadoComprobanteSri.ErrorServicio,
            informacionAdicional.Length > 0 ? informacionAdicional : "El SRI no devolvió un estado reconocible.",
            xmlRespuesta);
    }

    private static string Texto(XElement padre, string nombreLocal) =>
        padre.Descendants().FirstOrDefault(e => e.Name.LocalName == nombreLocal)?.Value ?? string.Empty;
}
