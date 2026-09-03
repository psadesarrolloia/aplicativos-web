using System.Text.Json;

namespace PsaWeb.Datil;

/// <summary>
/// Opciones de serialización para el API de Datil: <c>snake_case</c> y sin nulos
/// (equivalente al <c>SnakeCaseContractResolver</c> + <c>NullValueHandling.Ignore</c>
/// del cliente original).
/// </summary>
public static class DatilJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null, // las claves de InformacionAdicional van tal cual
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
