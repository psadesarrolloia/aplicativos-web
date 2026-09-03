using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

[Table("EPoofGeneralAditionalInfo")]
public partial class EpoofGeneralAditionalInfo
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("codDoc")]
    [StringLength(2)]
    [Unicode(false)]
    public string CodDoc { get; set; } = null!;

    [Column("RUC")]
    [StringLength(13)]
    public string Ruc { get; set; } = null!;

    [StringLength(300)]
    public string Nombre { get; set; } = null!;

    public int OrderNum { get; set; }

    [StringLength(300)]
    public string? ValueAllTime { get; set; }

    [ForeignKey("Ruc")]
    [InverseProperty("EpoofGeneralAditionalInfo")]
    public virtual Transmitter RucNavigation { get; set; } = null!;
}
