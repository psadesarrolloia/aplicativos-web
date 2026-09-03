using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PsaWeb.Datil;
using PsaWeb.Datil.Model;

namespace PsaWeb.Datil.Tests;

public class DatilClientTests
{
    private static readonly DatilCredentials Cred =
        new("api-key-123", "pass-456", "https://link.datil.co/retenciones/");

    private static Retencion Doc() => new() { Secuencial = "1", PeriodoFiscal = "08/2026" };

    private static DatilClient Build(HttpMessageHandler handler, bool dryRun) =>
        new(new HttpClient(handler),
            Options.Create(new DatilOptions { DryRun = dryRun }),
            NullLogger<DatilClient>.Instance);

    [Fact]
    public async Task DryRun_no_hace_HTTP_y_devuelve_el_json()
    {
        var client = Build(new ThrowingHandler(), dryRun: true);

        var result = await client.EmitirRetencionAsync(Doc(), Cred);

        Assert.True(result.FueDryRun);
        Assert.False(result.Emitido);
        Assert.Contains("periodo_fiscal", result.RawResponse);
    }

    [Fact]
    public async Task Emision_ok_extrae_id_y_manda_las_cabeceras()
    {
        var stub = new StubHandler("""{"id":"abc123","clave_acceso":"CLAVE-1"}""");
        var client = Build(stub, dryRun: false);

        var result = await client.EmitirRetencionAsync(Doc(), Cred);

        Assert.True(result.Emitido);
        Assert.Equal("abc123", result.Id);
        Assert.Equal("CLAVE-1", result.ClaveAcceso);
        Assert.Empty(result.Errores);

        Assert.Equal(1, stub.Calls);
        Assert.EndsWith("/issue", stub.LastRequest!.RequestUri!.ToString());
        Assert.Equal("api-key-123", stub.LastRequest.Headers.GetValues("X-Key").Single());
        Assert.Equal("pass-456", stub.LastRequest.Headers.GetValues("X-Password").Single());
        Assert.Equal("psa-web", stub.LastRequest.Headers.GetValues("X-Dat-Channel").Single());
    }

    [Fact]
    public async Task Emision_con_errores_de_string()
    {
        var stub = new StubHandler("""{"errors":["RUC inválido","Secuencial repetido"]}""", HttpStatusCode.BadRequest);
        var client = Build(stub, dryRun: false);

        var result = await client.EmitirRetencionAsync(Doc(), Cred);

        Assert.False(result.Emitido);
        Assert.Equal(new[] { "RUC inválido", "Secuencial repetido" }, result.Errores);
    }

    [Fact]
    public void Parse_errores_de_objeto_extrae_message()
    {
        var result = DatilClient.ParseEmision("""{"errors":[{"message":"campo requerido: sujeto"}]}""");

        Assert.False(result.Emitido);
        Assert.Equal("campo requerido: sujeto", Assert.Single(result.Errores));
    }

    [Fact]
    public void Parse_respuesta_no_json()
    {
        var result = DatilClient.ParseEmision("<html>502</html>");

        Assert.False(result.Emitido);
        Assert.Single(result.Errores);
    }

    [Fact]
    public async Task ConsultarEstado_devuelve_el_campo_estado()
    {
        var stub = new StubHandler("""{"estado":"AUTORIZADO"}""");
        var client = Build(stub, dryRun: false);

        var estado = await client.ConsultarEstadoAsync("abc123", Cred);

        Assert.Equal("AUTORIZADO", estado);
        Assert.EndsWith("/abc123", stub.LastRequest!.RequestUri!.ToString());
    }
}
