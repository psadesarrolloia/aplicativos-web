using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

[Table("DatilAPI")]
public partial class DatilApi
{
    [Key]
    [Column("dcId")]
    public int DcId { get; set; }

    [Column("myApiKey")]
    [StringLength(50)]
    public string MyApiKey { get; set; } = null!;

    [Column("mySignaturePassword")]
    [StringLength(50)]
    public string MySignaturePassword { get; set; } = null!;

    [StringLength(50)]
    public string ApiFacturaUrl { get; set; } = null!;

    [StringLength(50)]
    public string ApiRetencionUrl { get; set; } = null!;

    [Column("ApiNCUrl")]
    [StringLength(50)]
    public string ApiNcurl { get; set; } = null!;

    [Column("RUC")]
    [StringLength(13)]
    public string Ruc { get; set; } = null!;

    [ForeignKey("Ruc")]
    [InverseProperty("DatilApi")]
    public virtual Transmitter RucNavigation { get; set; } = null!;
}
