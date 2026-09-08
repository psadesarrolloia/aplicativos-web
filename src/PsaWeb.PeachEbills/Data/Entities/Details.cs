using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PsaWeb.PeachEbills.Data;

/// <summary>Línea (ítem) de un comprobante de venta (tabla <c>Details</c>).</summary>
public partial class Details
{
    [Key]
    public int DetailId { get; set; }

    [StringLength(300)]
    public string Description { get; set; } = null!;

    [StringLength(25)]
    public string? MainCode { get; set; }

    [StringLength(25)]
    public string? AuxCode { get; set; }

    public double Quantity { get; set; } = 1;

    public double UnitPrice { get; set; }

    public double Discount { get; set; }

    public double AmountWithoutTAX { get; set; }

    [Column(TypeName = "char")]
    [StringLength(1)]
    public string IVACode { get; set; } = "2";

    [Column(TypeName = "char")]
    [StringLength(1)]
    public string PercentIVACode { get; set; } = "2";

    public double AmountForIVA { get; set; }

    public double IVAValue { get; set; }

    public double IVAPercent { get; set; }

    /// <summary>FK a <see cref="Facturas.FacturaId"/>.</summary>
    public int Factura { get; set; }

    [StringLength(50)]
    public string? AccItemID { get; set; }

    [StringLength(50)]
    public string? AccAccountID { get; set; }

    [StringLength(50)]
    public string? AccTwhRFItemID { get; set; }

    [StringLength(50)]
    public string? AccTwhRIVAItemID { get; set; }
}
