using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PsaWeb.PeachEbills.Data;

/// <summary>
/// Diccionario de tipos de identificación para el ATS (tabla
/// <c>dicIdentityTypeATS</c> de PeachEBills): mapea el código guardado en
/// Sage 50 (<see cref="ProofTypeId"/>, viene de <c>Vendors.OurAccountWithThem</c>
/// / <c>Customers.AccountNumber</c>) al código de tipo de identificación que
/// exige el esquema del ATS (<see cref="IdAts"/>), separado por tipo de
/// transacción (<see cref="TransType"/>: 1 = ventas, 2 = compras). Lo usa el
/// lector de compras del ATS (port de <c>LoadVendor</c>, filtra
/// <c>TransType == 2</c>).
/// </summary>
[Table("dicIdentityTypeATS")]
public partial class DicIdentityTypeAts
{
    [Key]
    [Column("Id")]
    public int Id { get; set; }

    [Column("IdAts")]
    [StringLength(2)]
    public string IdAts { get; set; } = null!;

    [Column("ProofTypeId")]
    [StringLength(2)]
    public string ProofTypeId { get; set; } = null!;

    [Column("TransType")]
    public int TransType { get; set; }
}
