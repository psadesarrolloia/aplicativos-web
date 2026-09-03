using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

public partial class Transmitter
{
    [Key]
    [StringLength(13)]
    public string Ruc { get; set; } = null!;

    [StringLength(300)]
    public string Name { get; set; } = null!;

    [StringLength(300)]
    public string NameAlias { get; set; } = null!;

    [StringLength(300)]
    public string Address { get; set; } = null!;

    [Column("Number_Resolution_CE")]
    [StringLength(300)]
    public string? NumberResolutionCe { get; set; }

    public bool HaveToDoAccounting { get; set; }

    public bool ToSendToDatil { get; set; }

    [Column("EBillsHavedo")]
    public bool EbillsHavedo { get; set; }

    public bool HaveSendTransmitterEmail { get; set; }

    [InverseProperty("RucNavigation")]
    public virtual CurrentAmbient? CurrentAmbient { get; set; }

    [InverseProperty("RucNavigation")]
    public virtual ICollection<DatilApi> DatilApi { get; set; } = new List<DatilApi>();

    [InverseProperty("RucNavigation")]
    public virtual ICollection<EpoofGeneralAditionalInfo> EpoofGeneralAditionalInfo { get; set; } = new List<EpoofGeneralAditionalInfo>();

    [InverseProperty("RucNavigation")]
    public virtual ICollection<Establishments> Establishments { get; set; } = new List<Establishments>();

    [InverseProperty("RucNavigation")]
    public virtual ICollection<PeachConnString> PeachConnString { get; set; } = new List<PeachConnString>();

    [InverseProperty("RuctransmitterNavigation")]
    public virtual ICollection<Persons> Persons { get; set; } = new List<Persons>();

    [InverseProperty("TransmitterRucNavigation")]
    public virtual ICollection<TaxWithHoldings> TaxWithHoldings { get; set; } = new List<TaxWithHoldings>();

    [InverseProperty("TransmitterRucNavigation")]
    public virtual ICollection<TransmitterStatus> TransmitterStatus { get; set; } = new List<TransmitterStatus>();
}
