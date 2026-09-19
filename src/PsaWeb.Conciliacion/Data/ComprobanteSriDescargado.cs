using System.ComponentModel.DataAnnotations;

namespace PsaWeb.Conciliacion.Data;

/// <summary>
/// Un comprobante recibido del SRI, tal como llega en el reporte que descarga
/// la extensión de Chrome de "Comprobantes electrónicos recibidos" — ya trae
/// tipo, proveedor, fechas y montos, no hace falta hidratarlo con otra fuente.
/// El único dato que no trae el reporte es el estado (vigente/anulado):
/// <see cref="Estado"/>/<see cref="FechaVerificacionEstado"/> se llenan
/// después, bajo demanda, contra el web service del SRI.
/// </summary>
public class ComprobanteSriDescargado
{
    public long Id { get; set; }

    /// <summary>RUC de la empresa que concilia (el receptor del comprobante).</summary>
    [MaxLength(13)]
    public string Ruc { get; set; } = string.Empty;

    /// <summary>Clave de acceso del SRI, 49 dígitos. Única por <see cref="Ruc"/>.</summary>
    [MaxLength(49)]
    public string ClaveAcceso { get; set; } = string.Empty;

    [MaxLength(13)]
    public string RucEmisor { get; set; } = string.Empty;

    [MaxLength(300)]
    public string RazonSocialEmisor { get; set; } = string.Empty;

    /// <summary>Texto tal cual del reporte ("Factura", "Nota de Crédito", ...).</summary>
    [MaxLength(40)]
    public string TipoComprobante { get; set; } = string.Empty;

    /// <summary>"001-012-024129728" (establecimiento-puntoEmision-secuencial).</summary>
    [MaxLength(20)]
    public string SerieComprobante { get; set; } = string.Empty;

    public DateTime FechaAutorizacion { get; set; }
    public DateOnly FechaEmision { get; set; }

    [MaxLength(13)]
    public string IdentificacionReceptor { get; set; } = string.Empty;

    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }

    /// <summary>Serie de la factura que modifica (solo notas de crédito). Vacío en el resto.</summary>
    [MaxLength(20)]
    public string? NumeroDocumentoModificado { get; set; }

    /// <summary>Estado devuelto por el SRI en la última verificación (§4.5). Null = nunca verificado.</summary>
    [MaxLength(30)]
    public string? Estado { get; set; }

    public DateTime? FechaVerificacionEstado { get; set; }

    /// <summary>
    /// El revisor marcó la diferencia (Subtotal/IVA/Total o fecha/RUC emisor)
    /// como aceptada — ej. corresponde a ICE, propina, o un ajuste conocido —
    /// para no tener que revisarla de nuevo en cada conciliación.
    /// </summary>
    public bool DiferenciaAceptada { get; set; }

    [MaxLength(450)]
    public string? DiferenciaAceptadaPor { get; set; }

    public DateTime? DiferenciaAceptadaUtc { get; set; }

    [MaxLength(500)]
    public string? ComentarioAceptacion { get; set; }

    public DateTime FechaDescargaUtc { get; set; } = DateTime.UtcNow;

    /// <summary><c>UsuarioId</c> de Identity del que subió el reporte.</summary>
    [MaxLength(450)]
    public string SubidoPor { get; set; } = string.Empty;
}
