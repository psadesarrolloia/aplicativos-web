using System.Security;
using PsaWeb.Compras.Sri;

namespace PsaWeb.Compras.Tests;

public class LectorFacturaSriTests
{
    private static readonly string Sintetica = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "factura-sintetica.xml"));

    // Sin la declaración <?xml?> (dentro de un sobre no puede ir como texto escapado tal cual en CDATA sí).
    private static string SinDeclaracion => Sintetica[(Sintetica.IndexOf("?>", StringComparison.Ordinal) + 2)..];

    [Fact]
    public void Lee_la_factura_suelta()
    {
        var r = LectorFacturaSri.Leer(Sintetica);

        Assert.Null(r.Error);
        var f = r.Factura!;
        Assert.Equal("001-002-000000123", f.NumeroCompleto);
        Assert.Equal("0101202601179999999900120010020000001231234567811", f.ClaveAcceso);
        Assert.Equal(new DateTime(2026, 1, 1), f.FechaEmision);
        Assert.Equal(2, f.Ambiente);
        Assert.Equal("1799999999001", f.Emisor.Ruc);
        Assert.Equal("5368", f.Emisor.ContribuyenteEspecial);
        Assert.True(f.Emisor.ObligadoContabilidad);
        Assert.Equal("1799999998001", f.Comprador.Identificacion);
        Assert.Equal(130.50m, f.Totales.ImporteTotal);
        Assert.Equal("19", Assert.Single(f.Pagos!).FormaPago);
    }

    [Fact]
    public void Cantidad_cero_pasa_a_uno_con_precio_igual_al_total()
    {
        var d = LectorFacturaSri.Leer(Sintetica).Factura!.Detalles[1];

        Assert.Equal(1m, d.Cantidad);
        Assert.Equal(10.50m, d.PrecioUnitario);
        Assert.Equal("AUX", d.CodigoAuxiliar);
    }

    [Fact]
    public void Solo_impuestos_totales_con_valor_y_tarifa_tomada_del_detalle()
    {
        var t = Assert.Single(LectorFacturaSri.Leer(Sintetica).Factura!.Totales.Impuestos);

        Assert.Equal(("2", "4", 100m, 15m, (decimal?)15m), (t.Codigo, t.CodigoPorcentaje, t.BaseImponible, t.Valor, t.Tarifa));
    }

    [Fact]
    public void Propina_suma_todos_los_otros_rubros_de_terceros()
    {
        // Corrección C1: el `.exe` sumaba solo el primer rubro (2 + 1.50).
        Assert.Equal(5.00m, LectorFacturaSri.Leer(Sintetica).Factura!.Totales.Propina);
    }

    [Fact]
    public void Desenvuelve_la_respuesta_soap_del_ws_de_autorizacion()
    {
        var soap = $"""
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body>
            <ns2:autorizacionComprobanteResponse xmlns:ns2="http://ec.gob.sri.ws.autorizacion"><RespuestaAutorizacionComprobante>
            <autorizaciones><autorizacion><estado>AUTORIZADO</estado><comprobante>{SecurityElement.Escape(Sintetica)}</comprobante>
            </autorizacion></autorizaciones></RespuestaAutorizacionComprobante></ns2:autorizacionComprobanteResponse></soap:Body></soap:Envelope>
            """;

        var r = LectorFacturaSri.Leer(soap);

        Assert.Null(r.Error);
        Assert.Equal("001-002-000000123", r.Factura!.NumeroCompleto);
    }

    [Fact]
    public void Desenvuelve_el_archivo_autorizacion_con_cdata_del_portal()
    {
        var portal = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><autorizacion><estado>AUTORIZADO</estado>" +
                     $"<comprobante><![CDATA[{Sintetica}]]></comprobante></autorizacion>";

        Assert.Equal("001-002-000000123", LectorFacturaSri.Leer(portal).Factura!.NumeroCompleto);
    }

    [Fact]
    public void Rechaza_otros_tipos_y_comprobantes_sin_id()
    {
        var retencion = SinDeclaracion.Replace("<factura ", "<comprobanteRetencion ").Replace("</factura>", "</comprobanteRetencion>");
        var r = LectorFacturaSri.Leer(retencion);
        Assert.Null(r.Factura);
        Assert.Equal(TipoComprobanteSri.Retencion, r.Tipo);
        Assert.Equal("Se ha reconocido un comprobante electrónico de tipo : Retención", r.Error);

        var sinId = LectorFacturaSri.Leer(SinDeclaracion.Replace("id=\"comprobante\"", ""));
        Assert.Equal(TipoComprobanteSri.NoValido, sinId.Tipo);
        Assert.Contains("id = \"comprobante\"", sinId.Error);

        Assert.Equal(TipoComprobanteSri.NoValido, LectorFacturaSri.Leer("no es xml").Tipo);
    }
}
