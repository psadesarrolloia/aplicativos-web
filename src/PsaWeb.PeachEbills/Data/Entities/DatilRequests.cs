using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

public partial class DatilRequests
{
    [Key]
    public int Id { get; set; }

    public bool IsTaxWithH { get; set; }

    public int? RefId { get; set; }

    [StringLength(50)]
    public string PostOrder { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime DateRequest { get; set; }

    [Column(TypeName = "ntext")]
    public string DatilRequest { get; set; } = null!;

    [Column("RUC")]
    [StringLength(13)]
    public string? Ruc { get; set; }

    [Column("user")]
    [StringLength(50)]
    public string? User { get; set; }
}
