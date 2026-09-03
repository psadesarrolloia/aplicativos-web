using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

public partial class Establishments
{
    [Key]
    public int EstablishmentId { get; set; }

    [StringLength(13)]
    public string Ruc { get; set; } = null!;

    [StringLength(3)]
    [Unicode(false)]
    public string Code { get; set; } = null!;

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(3)]
    [Unicode(false)]
    public string IssuePoint { get; set; } = null!;

    public bool IsFromPeach { get; set; }

    [Column("startNumerationSell")]
    public int? StartNumerationSell { get; set; }

    [Column("startNumerationTwh")]
    public int? StartNumerationTwh { get; set; }

    [Column("startNumerationPurchaseLiq")]
    public int? StartNumerationPurchaseLiq { get; set; }

    [Column("startNumerationCreditMemo")]
    public int? StartNumerationCreditMemo { get; set; }

    public bool IsElectronic { get; set; }

    [ForeignKey("Ruc")]
    [InverseProperty("Establishments")]
    public virtual Transmitter RucNavigation { get; set; } = null!;

    [InverseProperty("TransmitterEstablishmentNavigation")]
    public virtual ICollection<TaxWithHoldings> TaxWithHoldings { get; set; } = new List<TaxWithHoldings>();
}
