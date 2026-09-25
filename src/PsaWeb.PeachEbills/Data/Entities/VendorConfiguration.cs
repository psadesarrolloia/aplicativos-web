using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PsaWeb.PeachEbills.Data;

/// <summary>
/// Relación aprendida «código del proveedor → ítem de Sage» del registro de compras (tabla <c>VendorConfiguration</c>),
/// por empresa (<see cref="TransmitterRuc"/>) y proveedor (<see cref="VendorRuc"/>).
/// </summary>
[Table("VendorConfiguration")]
public partial class VendorConfiguration
{
    [Key]
    public int ConfigVendorId { get; set; }

    [Column("TransmitterRUC")]
    [StringLength(13)]
    public string TransmitterRuc { get; set; } = null!;

    [StringLength(50)]
    public string VendorRuc { get; set; } = null!;

    [StringLength(50)]
    public string VendorCode { get; set; } = null!;

    [StringLength(20)]
    public string SageItemId { get; set; } = null!;
}
