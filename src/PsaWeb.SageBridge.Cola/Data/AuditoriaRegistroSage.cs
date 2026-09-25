using System.ComponentModel.DataAnnotations;

namespace PsaWeb.SageBridge.Cola.Data;

/// <summary>
/// Auditoría de lo que los módulos web mandan a registrar en Sage (§9 del plan de la Ola 2): una fila por acción del usuario
/// (encolar el guardado de una OC, rechazo por validación…). El resultado final lo tiene el trabajo (<see cref="TrabajoId"/>).
/// </summary>
public sealed class AuditoriaRegistroSage
{
    public long Id { get; set; }

    [MaxLength(13)] public string Ruc { get; set; } = string.Empty;

    /// <summary><c>Compras</c>, <c>RetencionesRecibidas</c>…</summary>
    [MaxLength(40)] public string Modulo { get; set; } = string.Empty;

    /// <summary><c>GuardarOc</c>, <c>RechazoValidacion</c>…</summary>
    [MaxLength(40)] public string Accion { get; set; } = string.Empty;

    [MaxLength(256)] public string Usuario { get; set; } = string.Empty;

    public DateTime FechaUtc { get; set; }

    /// <summary>Documento de origen (nº de factura <c>001-001-000000123</c>).</summary>
    [MaxLength(100)] public string Documento { get; set; } = string.Empty;

    /// <summary>ID del proveedor/cliente en Sage.</summary>
    [MaxLength(40)] public string Tercero { get; set; } = string.Empty;

    /// <summary>Nº de OC, PostOrder o clave de acceso según la acción.</summary>
    [MaxLength(100)] public string? Referencia { get; set; }

    public long? TrabajoId { get; set; }

    [MaxLength(40)] public string Resultado { get; set; } = string.Empty;

    [MaxLength(2000)] public string? Detalle { get; set; }

    /// <summary>SHA-256 (hex, 64) del payload enviado al Bridge.</summary>
    [MaxLength(64)] public string? HuellaPayload { get; set; }

    /// <summary>SHA-256 del XML del SRI de origen (F5).</summary>
    [MaxLength(64)] public string? HuellaOrigen { get; set; }
}
