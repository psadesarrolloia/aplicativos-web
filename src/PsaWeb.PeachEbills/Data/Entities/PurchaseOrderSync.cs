using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

public partial class PurchaseOrderSync
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("POPostOrder")]
    [StringLength(50)]
    public string PopostOrder { get; set; } = null!;

    [Column("PIPostOrder")]
    [StringLength(50)]
    public string PipostOrder { get; set; } = null!;

    [Column("RUCTransmitter")]
    [StringLength(13)]
    public string Ructransmitter { get; set; } = null!;
}
