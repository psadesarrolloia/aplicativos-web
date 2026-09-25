using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PsaWeb.PeachEbills.Data;

/// <summary>
/// Opciones de «forma de pago / retención» del registro de compras (tabla <c>AboutApplyTwh</c>): tarjeta (1), débito
/// autorizado (2), otros (5), retención asumida (7)… <see cref="HasTwh"/> indica si la compra lleva retención.
/// </summary>
[Table("AboutApplyTwh")]
public partial class AboutApplyTwh
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    [StringLength(50)]
    public string Description { get; set; } = null!;

    [StringLength(50)]
    public string? PaymentDatilCodigo { get; set; }

    [Column("hasTwh")]
    public bool HasTwh { get; set; }
}
