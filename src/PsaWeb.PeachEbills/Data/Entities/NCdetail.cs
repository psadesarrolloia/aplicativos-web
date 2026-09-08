using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PsaWeb.PeachEbills.Data;

/// <summary>
/// Datos del documento modificado por una nota de crédito (tabla <c>NCdetail</c>).
/// La PK <see cref="NCid"/> es el <see cref="Facturas.FacturaId"/> de la nota.
/// </summary>
[Table("NCdetail")]
public partial class NcDetail
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int NCid { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime DateBill { get; set; }

    [StringLength(20)]
    public string BillNumber { get; set; } = null!;

    [Column(TypeName = "char")]
    [StringLength(2)]
    public string BillCodeDoc { get; set; } = null!;

    [StringLength(300)]
    public string Cause { get; set; } = null!;
}
