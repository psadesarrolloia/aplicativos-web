using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PsaWeb.PeachEbills.Data;

/// <summary>Tipos de identificación del SRI (tabla <c>IdentityType</c>): 04 RUC, 05 cédula, 06 pasaporte…</summary>
[Table("IdentityType")]
public partial class IdentityType
{
    [Key]
    [StringLength(2)]
    public string ProofTypeId { get; set; } = null!;

    [StringLength(50)]
    public string Descr { get; set; } = null!;
}
