using System.ComponentModel.DataAnnotations;

namespace PsaWeb.Conciliacion.Data;

/// <summary>
/// Un hallazgo de la conciliación que un revisor marcó como "Aceptada / Revisada OK" (con
/// comentario obligatorio) para no volver a revisarlo en cada consulta. Va en una tabla propia y no
/// en <see cref="ComprobanteSriDescargado"/> porque las filas "Solo en Sage" no tienen comprobante
/// del SRI donde guardarlo.
/// </summary>
public class RevisionConciliacion
{
    public long Id { get; set; }

    [MaxLength(13)]
    public string Ruc { get; set; } = string.Empty;

    /// <summary>Identifica la fila: <see cref="ClaveRevision"/> ("C:&lt;clave de acceso&gt;" o "P:&lt;PostOrder de Sage&gt;").</summary>
    [MaxLength(60)]
    public string Clave { get; set; } = string.Empty;

    /// <summary>
    /// Huella del hallazgo al momento de revisarlo (clasificación + diferencias). Si después cambia
    /// (p. ej. se editó el monto en Sage), la revisión deja de aplicar y la fila vuelve a quedar por
    /// revisar. Vacía = revisión heredada de antes de existir la huella: aplica siempre.
    /// </summary>
    [MaxLength(600)]
    public string Huella { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Comentario { get; set; } = string.Empty;

    /// <summary>Usuario que la revisó.</summary>
    [MaxLength(450)]
    public string RevisadaPor { get; set; } = string.Empty;

    public DateTime RevisadaUtc { get; set; } = DateTime.UtcNow;
}
