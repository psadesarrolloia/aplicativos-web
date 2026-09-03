namespace PsaWeb.Comprobantes.Proveedores;

/// <summary>Proveedor leído de Sage 50 y mapeado a los campos que pide el SRI.</summary>
public sealed class ProveedorSri
{
    public string Identificacion { get; init; } = string.Empty;
    public string TipoIdentificacion { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
    public string Direccion { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Telefono { get; init; }

    /// <summary>Problemas encontrados al mapear/validar. Vacío = proveedor válido.</summary>
    public IReadOnlyList<string> Errores { get; init; } = Array.Empty<string>();

    public bool Ok => Errores.Count == 0;
}
