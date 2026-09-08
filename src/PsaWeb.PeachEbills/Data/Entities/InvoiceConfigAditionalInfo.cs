using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PsaWeb.PeachEbills.Data;

/// <summary>
/// Configuración por empresa de la información adicional que se agrega a las
/// facturas electrónicas (tabla <c>InvoiceConfigAditionalInfo</c>). Cada fila
/// define un campo: nombre, orden y de dónde sale el valor (tabla/columna de
/// Sage 50, o un valor fijo en <see cref="ValueAllTime"/>).
/// </summary>
public partial class InvoiceConfigAditionalInfo
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("RUC")]
    [StringLength(13)]
    public string Ruc { get; set; } = null!;

    [StringLength(300)]
    public string Nombre { get; set; } = null!;

    /// <summary>Tabla de Sage 50 de donde sale el valor (JrnlHdr / JrnlRow / Customers). Null = valor fijo.</summary>
    [StringLength(50)]
    public string? SourceTable { get; set; }

    /// <summary>Columna/expresión dentro de <see cref="SourceTable"/>.</summary>
    [StringLength(50)]
    public string? SourceValue { get; set; }

    public int OrderNum { get; set; }

    /// <summary>Valor fijo cuando no hay <see cref="SourceTable"/>.</summary>
    [StringLength(300)]
    public string? ValueAllTime { get; set; }
}
