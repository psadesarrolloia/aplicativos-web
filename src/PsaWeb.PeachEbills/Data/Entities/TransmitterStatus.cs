using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

public partial class TransmitterStatus
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("TransmitterRUC")]
    [StringLength(13)]
    public string TransmitterRuc { get; set; } = null!;

    public bool IsActive { get; set; }

    [ForeignKey("TransmitterRuc")]
    [InverseProperty("TransmitterStatus")]
    public virtual Transmitter TransmitterRucNavigation { get; set; } = null!;
}
