using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

public partial class PeachConnString
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("DSN")]
    [StringLength(50)]
    public string Dsn { get; set; } = null!;

    [StringLength(50)]
    public string Driver { get; set; } = null!;

    [Column("uid")]
    [StringLength(50)]
    public string Uid { get; set; } = null!;

    [Column("pwd")]
    [StringLength(50)]
    public string Pwd { get; set; } = null!;

    [Column("RUC")]
    [StringLength(13)]
    public string Ruc { get; set; } = null!;

    [Column("servername")]
    [StringLength(100)]
    public string? Servername { get; set; }

    [Column("dbq")]
    [StringLength(100)]
    public string? Dbq { get; set; }

    [Column("checkToLoadByRUC")]
    public bool CheckToLoadByRuc { get; set; }

    [ForeignKey("Ruc")]
    [InverseProperty("PeachConnString")]
    public virtual Transmitter RucNavigation { get; set; } = null!;
}
