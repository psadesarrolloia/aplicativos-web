using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PsaWeb.Datil.Model;

namespace PsaWeb.Datil;

internal sealed class DatilClient : IDatilClient
{
    private const string Channel = "psa-web";

    private readonly HttpClient _http;
    private readonly DatilOptions _options;
    private readonly ILogger<DatilClient> _logger;

    public DatilClient(HttpClient http, IOptions<DatilOptions> options, ILogger<DatilClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public Task<DatilEmisionResult> EmitirRetencionAsync(
        Retencion retencion, DatilCredentials credenciales, CancellationToken cancellationToken = default)
        => EnviarAsync(retencion, "retención", retencion.Secuencial, credenciales, cancellationToken);

    public Task<DatilEmisionResult> EmitirFacturaAsync(
        Factura factura, DatilCredentials credenciales, CancellationToken cancellationToken = default)
        => EnviarAsync(factura, "factura", factura.Secuencial, credenciales, cancellationToken);

    public Task<DatilEmisionResult> EmitirNotaCreditoAsync(
        NotaCredito notaCredito, DatilCredentials credenciales, CancellationToken cancellationToken = default)
        => EnviarAsync(notaCredito, "nota de crédito", notaCredito.Secuencial, credenciales, cancellationToken);

    public Task<DatilEmisionResult> EmitirLiquidacionAsync(
        Liquidacion liquidacion, DatilCredentials credenciales, CancellationToken cancellationToken = default)
        => EnviarAsync(liquidacion, "liquidación", liquidacion.Secuencial, credenciales, cancellationToken);

    private async Task<DatilEmisionResult> EnviarAsync<T>(
        T documento, string tipo, string secuencial, DatilCredentials credenciales, CancellationToken cancellationToken)
    {
        var body = DatilJson.Serialize(documento);

        if (_options.DryRun)
        {
            _logger.LogInformation(
                "Datil DryRun: {Tipo} secuencial {Secuencial} NO enviada. Cuerpo: {Body}",
                tipo, secuencial, body);
            return DatilEmisionResult.DryRun(body);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, credenciales.IssueUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        ApplyHeaders(request, credenciales);

        using var response = await _http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        return ParseEmision(raw);
    }

    public async Task<string?> ConsultarEstadoAsync(
        string id, DatilCredentials credenciales, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, credenciales.StatusUrl(id));
        ApplyHeaders(request, credenciales);

        using var response = await _http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.TryGetProperty("estado", out var estado) ? estado.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<DatilConsultaResult> ConsultarComprobanteAsync(
        string id, DatilCredentials credenciales, CancellationToken cancellationToken = default)
    {
        string raw;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, credenciales.StatusUrl(id));
            ApplyHeaders(request, credenciales);
            using var response = await _http.SendAsync(request, cancellationToken);
            raw = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return new DatilConsultaResult { Descripcion = "Sin respuesta de Datil.", RawResponse = ex.Message };
        }

        return ParseConsulta(raw);
    }

    internal static DatilConsultaResult ParseConsulta(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new DatilConsultaResult { Descripcion = "Sin respuesta de Datil.", RawResponse = raw };
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            return new DatilConsultaResult { Descripcion = "Sin respuesta del SRI", RawResponse = raw };
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.TryGetProperty("estado", out var estadoEl)
                && estadoEl.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(estadoEl.GetString()))
            {
                var estado = estadoEl.GetString()!;
                return new DatilConsultaResult
                {
                    Estado = estado,
                    Descripcion = estado.ToLowerInvariant(),
                    RawResponse = raw,
                };
            }

            if (root.TryGetProperty("errors", out var errsEl)
                && errsEl.ValueKind == JsonValueKind.Array
                && errsEl.GetArrayLength() > 0)
            {
                var first = errsEl[0];
                var msg = first.ValueKind == JsonValueKind.Object ? MensajeDeError(first)
                    : first.ValueKind == JsonValueKind.String ? first.GetString()
                    : first.GetRawText();
                return new DatilConsultaResult { Descripcion = msg ?? "error desconocido", RawResponse = raw };
            }

            return new DatilConsultaResult { Descripcion = "error desconocido", RawResponse = raw };
        }
    }

    private static void ApplyHeaders(HttpRequestMessage request, DatilCredentials cred)
    {
        request.Headers.TryAddWithoutValidation("X-Key", cred.ApiKey);
        request.Headers.TryAddWithoutValidation("X-Password", cred.Password);
        request.Headers.TryAddWithoutValidation("X-Dat-Channel", Channel);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    internal static DatilEmisionResult ParseEmision(string raw)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            return new DatilEmisionResult { Emitido = false, RawResponse = raw, Errores = new[] { "Respuesta no es JSON válido." } };
        }

        using (doc)
        {
            var root = doc.RootElement;

            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var clave = root.TryGetProperty("clave_acceso", out var cEl) ? cEl.GetString() : null;

            if (!string.IsNullOrWhiteSpace(id))
            {
                return new DatilEmisionResult
                {
                    Emitido = true,
                    Id = id,
                    ClaveAcceso = clave,
                    RawResponse = raw,
                };
            }

            var errores = new List<string>();
            if (root.TryGetProperty("errors", out var errsEl) && errsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in errsEl.EnumerateArray())
                {
                    errores.Add(e.ValueKind switch
                    {
                        JsonValueKind.String => e.GetString() ?? string.Empty,
                        JsonValueKind.Object => MensajeDeError(e),
                        _ => e.GetRawText(),
                    });
                }
            }
            if (errores.Count == 0)
            {
                errores.Add("Datil no devolvió 'id' ni 'errors'.");
            }

            return new DatilEmisionResult { Emitido = false, RawResponse = raw, Errores = errores };
        }
    }

    private static string MensajeDeError(JsonElement obj)
    {
        foreach (var name in new[] { "message", "mensaje", "detail", "detalle", "description" })
        {
            if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
            {
                return v.GetString() ?? obj.GetRawText();
            }
        }
        return obj.GetRawText();
    }
}
