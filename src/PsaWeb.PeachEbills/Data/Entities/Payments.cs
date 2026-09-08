using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PsaWeb.PeachEbills.Data;

/// <summary>Pago asociado a una factura (tabla <c>Payments</c>).</summary>
public partial class Payments
{
    [Key]
    public int PaymentId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime PaymentDate { get; set; }

    public int? PaymentType { get; set; }

    public double PaymentTotal { get; set; }

    public string? PaymentProperties { get; set; }

    /// <summary>FK a <see cref="Facturas.FacturaId"/>.</summary>
    public int Factura { get; set; }
}
