using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PsaWeb.Datil;
using PsaWeb.Datil.Model;

namespace PsaWeb.Datil.Tests;

public class ComprobantesVentaClientTests
{
    private static readonly DatilCredentials CredFactura =
        new("api-key-123", "pass-456", "https://link.datil.co/invoices/");

    private static DatilClient Build(HttpMessageHandler handler, bool dryRun) =>
        new(new HttpClient(handler),
            Options.Create(new DatilOptions { DryRun = dryRun }),
            NullLogger<DatilClient>.Instance);

    private static Factura Factura() => new()
    {
        Secuencial = "13538",
        Items = { new ItemComprobante { Descripcion = "x", Cantidad = 1, PrecioUnitario = 1, PrecioTotalSinImpuestos = 1 } },
    };

    [Fact]
    public async Task Factura_DryRun_no_hace_HTTP()
    {
        var client = Build(new ThrowingHandler(), dryRun: true);

        var result = await client.EmitirFacturaAsync(Factura(), CredFactura);

        Assert.True(result.FueDryRun);
        Assert.False(result.Emitido);
        Assert.Contains("\"secuencial\":\"13538\"", result.RawResponse);
    }

    [Fact]
    public async Task Factura_emision_ok_postea_a_issue_con_cabeceras()
    {
        var stub = new StubHandler("""{"id":"fac-1","clave_acceso":"CA-1"}""");
        var client = Build(stub, dryRun: false);

        var result = await client.EmitirFacturaAsync(Factura(), CredFactura);

        Assert.True(result.Emitido);
        Assert.Equal("fac-1", result.Id);
        Assert.Equal("CA-1", result.ClaveAcceso);
        Assert.Equal("https://link.datil.co/invoices/issue", stub.LastRequest!.RequestUri!.ToString());
        Assert.Equal("api-key-123", stub.LastRequest.Headers.GetValues("X-Key").Single());
        Assert.Equal("psa-web", stub.LastRequest.Headers.GetValues("X-Dat-Channel").Single());
    }

    [Fact]
    public async Task NotaCredito_y_Liquidacion_tambien_emiten()
    {
        var stub = new StubHandler("""{"id":"x-1"}""");
        var client = Build(stub, dryRun: false);

        var nc = await client.EmitirNotaCreditoAsync(
            new NotaCredito { Secuencial = "1" },
            new DatilCredentials("k", "p", "https://link.datil.co/credit-notes/"));
        Assert.True(nc.Emitido);
        Assert.EndsWith("/credit-notes/issue", stub.LastRequest!.RequestUri!.ToString());

        var liq = await client.EmitirLiquidacionAsync(
            new Liquidacion { Secuencial = "1" },
            new DatilCredentials("k", "p", "https://link.datil.co/purchase-settlements/"));
        Assert.True(liq.Emitido);
        Assert.EndsWith("/purchase-settlements/issue", stub.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Factura_errores_se_devuelven()
    {
        var stub = new StubHandler("""{"errors":["secuencial repetido"]}""", HttpStatusCode.BadRequest);
        var client = Build(stub, dryRun: false);

        var result = await client.EmitirFacturaAsync(Factura(), CredFactura);

        Assert.False(result.Emitido);
        Assert.Equal("secuencial repetido", Assert.Single(result.Errores));
    }

    [Fact]
    public async Task ConsultarComprobante_devuelve_estado_en_minusculas()
    {
        var stub = new StubHandler("""{"estado":"AUTORIZADO"}""");
        var client = Build(stub, dryRun: false);

        var r = await client.ConsultarComprobanteAsync("fac-1", CredFactura);

        Assert.Equal("AUTORIZADO", r.Estado);
        Assert.Equal("autorizado", r.Descripcion);
        Assert.EndsWith("/fac-1", stub.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public void ParseConsulta_sin_estado_toma_primer_error()
    {
        var r = DatilClient.ParseConsulta("""{"errors":[{"message":"clave de acceso ya registrada"}]}""");

        Assert.Null(r.Estado);
        Assert.Equal("clave de acceso ya registrada", r.Descripcion);
    }

    [Fact]
    public void ParseConsulta_respuesta_vacia_o_no_json()
    {
        Assert.Equal("Sin respuesta de Datil.", DatilClient.ParseConsulta("").Descripcion);
        Assert.Equal("Sin respuesta del SRI", DatilClient.ParseConsulta("<html>502</html>").Descripcion);
        Assert.Equal("error desconocido", DatilClient.ParseConsulta("{}").Descripcion);
    }
}
