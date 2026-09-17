using PsaWeb.Conciliacion;

namespace PsaWeb.Conciliacion.Tests;

/// <summary>
/// Parseo de las respuestas reales del WS grabadas en F0/F3 (2026-09-17,
/// contra <c>cel.sri.gob.ec</c> con una clave sintética y una real de CPTDC —
/// no se pega la clave real acá, mismo criterio del plan). No llama al WS de
/// verdad — eso ya se probó a mano en la sesión de diseño.
/// </summary>
public class ConsultaComprobanteClientTests
{
    // Respuesta real de un comprobante AUTORIZADO — nótese que NO trae
    // estadoConsulta (solo aparece en los casos de error, hallazgo de F3).
    private const string RespuestaAutorizado = """
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body>
          <ns2:consultarEstadoAutorizacionComprobanteResponse xmlns:ns2="http://ec.gob.sri.ws.consultas">
            <EstadoAutorizacionComprobante>
              <claveAcceso>0108202601179128754100120010120241297281526111115</claveAcceso>
              <mensajes/>
              <estadoAutorizacion>AUTORIZADO</estadoAutorizacion>
              <tipoComprobante>Factura</tipoComprobante>
              <rucEmisor>1791287541001</rucEmisor>
              <fechaAutorizacion>2026-08-01T03:24:52-05:00</fechaAutorizacion>
            </EstadoAutorizacionComprobante>
          </ns2:consultarEstadoAutorizacionComprobanteResponse>
        </soap:Body></soap:Envelope>
        """;

    private const string RespuestaFormatoInvalido = """
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><ns2:consultarEstadoAutorizacionComprobanteResponse xmlns:ns2="http://ec.gob.sri.ws.consultas"><EstadoAutorizacionComprobante><estadoConsulta>RECHAZADA</estadoConsulta><claveAcceso>0000000000000000000000000000000000000000000000000</claveAcceso><mensajes><mensaje><identificador>99</identificador><mensaje>ERROR AL CONSULTAR DATOS DEL SERVICIO WEB</mensaje><informacionAdicional>Error al consultar la clave acceso. Error en el formato de la clave acceso.</informacionAdicional><tipo>ERROR</tipo></mensaje></mensajes></EstadoAutorizacionComprobante></ns2:consultarEstadoAutorizacionComprobanteResponse></soap:Body></soap:Envelope>
        """;

    private const string RespuestaNoEncontrado = """
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><ns2:consultarEstadoAutorizacionComprobanteResponse xmlns:ns2="http://ec.gob.sri.ws.consultas"><EstadoAutorizacionComprobante><estadoConsulta>RECHAZADA</estadoConsulta><claveAcceso>1509202601179205180000110010010000000011234567811</claveAcceso><mensajes><mensaje><identificador>99</identificador><mensaje>ERROR AL CONSULTAR DATOS DEL SERVICIO WEB</mensaje><informacionAdicional>No existen datos para los parámetros ingresados</informacionAdicional><tipo>ERROR</tipo></mensaje></mensajes></EstadoAutorizacionComprobante></ns2:consultarEstadoAutorizacionComprobanteResponse></soap:Body></soap:Envelope>
        """;

    [Fact]
    public void Comprobante_autorizado_se_reconoce_aunque_no_traiga_estadoConsulta()
    {
        var resultado = ConsultaComprobanteClient.InterpretarRespuesta(RespuestaAutorizado);

        Assert.Equal(EstadoComprobanteSri.Autorizado, resultado.Estado);
    }

    [Fact]
    public void Clave_con_formato_invalido_se_distingue_de_no_encontrado()
    {
        var resultado = ConsultaComprobanteClient.InterpretarRespuesta(RespuestaFormatoInvalido);

        Assert.Equal(EstadoComprobanteSri.FormatoInvalido, resultado.Estado);
    }

    [Fact]
    public void Clave_bien_formada_pero_inexistente_es_NoEncontrado()
    {
        var resultado = ConsultaComprobanteClient.InterpretarRespuesta(RespuestaNoEncontrado);

        Assert.Equal(EstadoComprobanteSri.NoEncontrado, resultado.Estado);
    }

    [Fact]
    public void Respuesta_no_reconocible_es_ErrorServicio_sin_tirar_excepcion()
    {
        var resultado = ConsultaComprobanteClient.InterpretarRespuesta("<xml sin sentido/>");

        Assert.Equal(EstadoComprobanteSri.ErrorServicio, resultado.Estado);
    }

    [Fact]
    public void Estado_no_autorizado_distinto_de_AUTORIZADO_se_marca_como_NoAutorizado()
    {
        // No hay un caso real de "anulado" confirmado todavía (§14.1) — se
        // trata cualquier valor que no sea exactamente "AUTORIZADO" como no
        // vigente, por seguridad.
        const string respuesta = """
            <EstadoAutorizacionComprobante>
              <estadoAutorizacion>NO AUTORIZADO</estadoAutorizacion>
            </EstadoAutorizacionComprobante>
            """;

        var resultado = ConsultaComprobanteClient.InterpretarRespuesta(respuesta);

        Assert.Equal(EstadoComprobanteSri.NoAutorizado, resultado.Estado);
    }
}
