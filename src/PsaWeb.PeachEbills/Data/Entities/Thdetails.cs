using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

[Table("THDetails")]
public partial class Thdetails
{
    [Key]
    [Column("THDetailId")]
    public int ThdetailId { get; set; }

    [StringLength(25)]
    public string? Code { get; set; }

    [StringLength(5)]
    public string PercentCode { get; set; } = null!;

    public double Percent { get; set; }

    public double AmountInTaxes { get; set; }

    [Column("RTaxValue")]
    public double RtaxValue { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime PurchaseDate { get; set; }

    [StringLength(20)]
    public string PurchaseNumber { get; set; } = null!;

    [StringLength(2)]
    [Unicode(false)]
    public string PurchaseCodDoc { get; set; } = null!;

    public int TaxWithHolding { get; set; }

    [Column("AccItemID")]
    [StringLength(50)]
    public string? AccItemId { get; set; }

    [Column("AccAccountID")]
    [StringLength(50)]
    public string? AccAccountId { get; set; }

    [Column("AccAsumedAccountID")]
    [StringLength(50)]
    public string? AccAsumedAccountId { get; set; }

    [ForeignKey("TaxWithHolding")]
    [InverseProperty("Thdetails")]
    public virtual TaxWithHoldings TaxWithHoldingNavigation { get; set; } = null!;
}
