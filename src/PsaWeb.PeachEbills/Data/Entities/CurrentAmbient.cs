using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

public partial class CurrentAmbient
{
    public short AmbientDefault { get; set; }

    [Column("emailForTest")]
    [StringLength(50)]
    public string EmailForTest { get; set; } = null!;

    [Key]
    [Column("RUC")]
    [StringLength(13)]
    public string Ruc { get; set; } = null!;

    public bool Active { get; set; }

    [ForeignKey("Ruc")]
    [InverseProperty("CurrentAmbient")]
    public virtual Transmitter RucNavigation { get; set; } = null!;
}
