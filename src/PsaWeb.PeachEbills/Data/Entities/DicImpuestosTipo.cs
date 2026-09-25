using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PsaWeb.PeachEbills.Data;

/// <summary>Tipos de impuesto del SRI (tabla <c>dicImpuestosTipo</c>): 2 IVA, 3 ICE, 5 IRBPNR.</summary>
[Table("dicImpuestosTipo")]
public partial class DicImpuestosTipo
{
    [Key]
    [Column("id")]
    [StringLength(2)]
    public string Id { get; set; } = null!;

    [StringLength(50)]
    public string Name { get; set; } = null!;
}
