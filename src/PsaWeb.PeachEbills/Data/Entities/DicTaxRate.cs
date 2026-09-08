using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PsaWeb.PeachEbills.Data;

/// <summary>
/// Diccionario de tasas de IVA (tabla <c>dicTaxRate</c> de PeachEBills): relaciona
/// un texto de porcentaje (<c>"12%"</c>, <c>"15%"</c>…) con el código de porcentaje
/// del SRI (<see cref="Id"/>) y la tasa como fracción (<see cref="IvaRate"/>).
/// Lo usan las liquidaciones de compra.
/// </summary>
[Table("dicTaxRate")]
public partial class DicTaxRate
{
    [Key]
    [Column("id")]
    [StringLength(2)]
    public string Id { get; set; } = null!;

    [Column("Name")]
    [StringLength(50)]
    public string Name { get; set; } = null!;

    /// <summary>Tasa como fracción (0.12, 0.15…).</summary>
    public float IvaRate { get; set; }
}
