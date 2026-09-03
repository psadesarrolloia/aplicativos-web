namespace PsaWeb.Datil;

/// <summary>Resultado de emitir un comprobante en Datil.</summary>
public sealed class DatilEmisionResult
{
    /// <summary>true si Datil aceptó el comprobante y devolvió un <see cref="Id"/>.</summary>
    public bool Emitido { get; init; }

    /// <summary>true si no se llamó al API (modo <c>DryRun</c>).</summary>
    public bool FueDryRun { get; init; }

    /// <summary>Id externo del comprobante en Datil (para consultar el estado).</summary>
    public string? Id { get; init; }

    public string? ClaveAcceso { get; init; }

    /// <summary>Mensajes de error devueltos por Datil (vacío si <see cref="Emitido"/>).</summary>
    public IReadOnlyList<string> Errores { get; init; } = Array.Empty<string>();

    /// <summary>Cuerpo crudo de la respuesta (o el JSON enviado, en <c>DryRun</c>).</summary>
    public string RawResponse { get; init; } = string.Empty;

    public static DatilEmisionResult DryRun(string requestJson) => new()
    {
        FueDryRun = true,
        Emitido = false,
        RawResponse = requestJson,
    };
}
