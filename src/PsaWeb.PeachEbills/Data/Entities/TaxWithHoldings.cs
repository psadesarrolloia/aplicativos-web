using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

public partial class TaxWithHoldings
{
    [Key]
    [Column("THId")]
    public int Thid { get; set; }

    [StringLength(20)]
    public string NumberPech { get; set; } = null!;

    [StringLength(100)]
    public string PostOrderPeach { get; set; } = null!;

    [Column("secuencial")]
    [StringLength(9)]
    public string Secuencial { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime DateIssued { get; set; }

    public short Ambient { get; set; }

    public short IssueType { get; set; }

    [Column("FPeriodo")]
    [StringLength(10)]
    public string Fperiodo { get; set; } = null!;

    [StringLength(20)]
    public string Contact { get; set; } = null!;

    public int? TransmitterEstablishment { get; set; }

    [StringLength(13)]
    public string TransmitterRuc { get; set; } = null!;

    [Column("DatilID")]
    public string? DatilId { get; set; }

    public bool IsValid { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? ChangeIsValidDate { get; set; }

    public int? Factura { get; set; }

    public int TransType { get; set; }

    [Column("claveAcceso")]
    [StringLength(100)]
    public string? ClaveAcceso { get; set; }

    [ForeignKey("TransmitterRuc")]
    [InverseProperty("TaxWithHoldings")]
    public virtual Transmitter TransmitterRucNavigation { get; set; } = null!;
}
