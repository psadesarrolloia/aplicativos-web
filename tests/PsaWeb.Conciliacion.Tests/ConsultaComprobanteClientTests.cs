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

    // Respuestas reales del WS de producción del 2026-09-19 (claves omitidas).
    private const string RespuestaAnulado = """
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><ns2:consultarEstadoAutorizacionComprobanteResponse xmlns:ns2="http://ec.gob.sri.ws.consultas"><EstadoAutorizacionComprobante><claveAcceso>0308202601179218779600120010010000155541234567814</claveAcceso><mensajes/><estadoAutorizacion>ANULADO</estadoAutorizacion><tipoComprobante>Factura</tipoComprobante><rucEmisor>1792187796001</rucEmisor><fechaAutorizacion>2026-08-03T14:14:25-05:00</fechaAutorizacion></EstadoAutorizacionComprobante></ns2:consultarEstadoAutorizacionComprobanteResponse></soap:Body></soap:Envelope>
        """;

    private const string RespuestaFueraDeRango = """
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><ns2:consultarEstadoAutorizacionComprobanteResponse xmlns:ns2="http://ec.gob.sri.ws.consultas"><EstadoAutorizacionComprobante><estadoConsulta>RECHAZADA</estadoConsulta><claveAcceso>2407201901179205180000120010010000018102793075810</claveAcceso><mensajes><mensaje><identificador>99</identificador><mensaje>ERROR AL CONSULTAR DATOS DEL SERVICIO WEB</mensaje><informacionAdicional>No es posible validar la clave de acceso ya que la fecha de emision esta fuera del rango permitido.</informacionAdicional><tipo>ERROR</tipo></mensaje></mensajes></EstadoAutorizacionComprobante></ns2:consultarEstadoAutorizacionComprobanteResponse></soap:Body></soap:Envelope>
        """;

    [Fact]
    public void Comprobante_ANULADO_es_Anulado_y_no_se_confunde_con_NoAutorizado()
    {
        var r = ConsultaComprobanteClient.InterpretarRespuesta(RespuestaAnulado);

        Assert.Equal(EstadoComprobanteSri.Anulado, r.Estado);
        Assert.Equal("ANULADO", r.MensajeSri);
        Assert.Contains("estadoAutorizacion", r.RespuestaCruda);
    }

    [Fact]
    public void Fecha_fuera_del_rango_del_WS_es_FueraDeRango_y_no_un_error_del_servicio()
    {
        var r = ConsultaComprobanteClient.InterpretarRespuesta(RespuestaFueraDeRango);

        Assert.Equal(EstadoComprobanteSri.FueraDeRango, r.Estado);
        Assert.Contains("fuera del rango", r.MensajeSri);
    }

    [Fact]
    public void Un_estado_desconocido_queda_como_Otro_con_la_respuesta_cruda_para_revisarlo()
    {
        const string respuesta = """
            <EstadoAutorizacionComprobante>
              <estadoAutorizacion>PENDIENTE DE ACEPTACION</estadoAutorizacion>
            </EstadoAutorizacionComprobante>
            """;

        var r = ConsultaComprobanteClient.InterpretarRespuesta(respuesta);

        Assert.Equal(EstadoComprobanteSri.Otro, r.Estado);
        Assert.Equal("PENDIENTE DE ACEPTACION", r.MensajeSri);
        Assert.Contains("PENDIENTE DE ACEPTACION", r.RespuestaCruda);
    }

    private sealed class ManejadorQueFallaLuegoResponde(int fallos, string cuerpo) : HttpMessageHandler
    {
        public int Llamadas;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Llamadas++;
            if (Llamadas <= fallos) throw new HttpRequestException("Could not establish trust relationship (simulado)");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(cuerpo) });
        }
    }

    [Fact]
    public async Task Reintenta_los_fallos_transitorios_de_red_y_termina_bien()
    {
        var manejador = new ManejadorQueFallaLuegoResponde(fallos: 2, cuerpo: RespuestaAutorizado);
        var cliente = new ConsultaComprobanteClient(
            new HttpClient(manejador), Microsoft.Extensions.Options.Options.Create(new ConciliacionOptions()));

        var r = await cliente.VerificarAsync("0108202601179128754100120010120241297281526111115");

        Assert.Equal(EstadoComprobanteSri.Autorizado, r.Estado);
        Assert.Equal(3, manejador.Llamadas);
    }

    [Fact]
    public async Task Si_los_tres_intentos_fallan_devuelve_ErrorServicio_sin_lanzar()
    {
        var manejador = new ManejadorQueFallaLuegoResponde(fallos: 99, cuerpo: string.Empty);
        var cliente = new ConsultaComprobanteClient(
            new HttpClient(manejador), Microsoft.Extensions.Options.Options.Create(new ConciliacionOptions()));

        var r = await cliente.VerificarAsync("0108202601179128754100120010120241297281526111115");

        Assert.Equal(EstadoComprobanteSri.ErrorServicio, r.Estado);
        Assert.Equal(3, manejador.Llamadas);
    }

    [Theory]
    [InlineData(null, "Sin verificar")]
    [InlineData("Autorizado", "Autorizado")]
    [InlineData("Anulado", "ANULADO")]
    [InlineData("FueraDeRango", "Fuera de rango del SRI")]
    [InlineData("Otro", "Otro estado (ver detalle)")]
    public void La_presentacion_del_estado_es_la_misma_para_Conciliacion_y_los_comprobantes(string? estado, string texto)
        => Assert.Equal(texto, EstadoSriPresentacion.Visual(estado).Texto);

    [Fact]
    public void Solo_Anulado_y_NoAutorizado_cuentan_como_no_vigentes()
    {
        Assert.True(EstadoSriPresentacion.NoVigente("Anulado"));
        Assert.True(EstadoSriPresentacion.NoVigente("NoAutorizado"));
        Assert.False(EstadoSriPresentacion.NoVigente("Autorizado"));
        Assert.False(EstadoSriPresentacion.NoVigente("FueraDeRango"));
        Assert.False(EstadoSriPresentacion.NoVigente(null));
    }
}
