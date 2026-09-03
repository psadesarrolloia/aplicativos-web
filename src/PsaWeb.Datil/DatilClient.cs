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

    public async Task<DatilEmisionResult> EmitirRetencionAsync(
        Retencion retencion, DatilCredentials credenciales, CancellationToken cancellationToken = default)
    {
        var body = DatilJson.Serialize(retencion);

        if (_options.DryRun)
        {
            _logger.LogInformation(
                "Datil DryRun: retención secuencial {Secuencial} NO enviada. Cuerpo: {Body}",
                retencion.Secuencial, body);
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
