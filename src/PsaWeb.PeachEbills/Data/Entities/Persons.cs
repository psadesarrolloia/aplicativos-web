using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

public partial class Persons
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("RUCtransmitter")]
    [StringLength(13)]
    public string Ructransmitter { get; set; } = null!;

    [StringLength(20)]
    public string PersonId { get; set; } = null!;

    [StringLength(300)]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Tipo de Identificación
    /// </summary>
    [StringLength(2)]
    public string Type { get; set; } = null!;

    [StringLength(300)]
    public string Email { get; set; } = null!;

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(300)]
    public string Address { get; set; } = null!;

    [StringLength(300)]
    public string? FaxNum { get; set; }

    [Column("vendorId")]
    [StringLength(20)]
    public string? VendorId { get; set; }

    [Column("customerId")]
    [StringLength(20)]
    public string? CustomerId { get; set; }

    public bool? LlevaContab { get; set; }

    [StringLength(20)]
    public string? ContribuyEsp { get; set; }

    [ForeignKey("Ructransmitter")]
    [InverseProperty("Persons")]
    public virtual Transmitter RuctransmitterNavigation { get; set; } = null!;
}
