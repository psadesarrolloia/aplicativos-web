using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PsaWeb.PeachEbills.Data;

/// <summary>Catálogo de formas de pago (tabla <c>PaymentTypes</c>).</summary>
public partial class PaymentTypes
{
    [Key]
    public int PaymentTypeId { get; set; }

    /// <summary>Código de forma de pago que espera Datil.</summary>
    [StringLength(50)]
    public string PaymentDatilCodigo { get; set; } = null!;

    [Column("PaymentSRICodigo", TypeName = "char")]
    [StringLength(2)]
    public string? PaymentSriCodigo { get; set; }

    [StringLength(50)]
    public string? Descripcion { get; set; }

    public int Orden { get; set; }
}
