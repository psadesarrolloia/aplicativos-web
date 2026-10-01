using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PsaWeb.PeachEbills.Data;

/// <summary>
/// Liquidación de importación guardada (tabla <c>ImportCost</c> del `.exe`, <c>FrmImportMng</c>): una por empresa y cuenta de
/// importación. El `.exe` insertaba un registro por cada «Guardar» y leía el último; la web actualiza el último en el lugar (C3).
/// </summary>
[Table("ImportCost")]
public partial class ImportCost
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>Cuenta de Sage de la importación (<c>IMPORTACION nn-aaaa</c>).</summary>
    [Column("accountId")]
    [StringLength(50)]
    public string AccountId { get; set; } = null!;

    [Column("vendorId")]
    [StringLength(50)]
    public string? VendorId { get; set; }

    /// <summary>PostOrder de la OC en Sage (texto).</summary>
    [Column("postOrderId")]
    [StringLength(50)]
    public string? PostOrderId { get; set; }

    /// <summary>GUID de la OC en el SDK.</summary>
    [Column("postOrderStrKey")]
    [StringLength(50)]
    public string? PostOrderStrKey { get; set; }

    /// <summary>Referencia de la OC (<c>LIQ IMPORT-041-2026</c>).</summary>
    [Column("postOrderNumber")]
    [StringLength(50)]
    public string? PostOrderNumber { get; set; }

    [Column("postOrderSequence")]
    [StringLength(50)]
    public string? PostOrderSequence { get; set; }

    [StringLength(13)]
    public string? TransmitterRuc { get; set; }

    public DateTime? TransactionDate { get; set; }
}

/// <summary>Ítem prorrateado de una liquidación (tabla <c>ImportCostApportion</c>).</summary>
[Table("ImportCostApportion")]
public partial class ImportCostApportion
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("importCost")]
    public int ImportCostId { get; set; }

    [Column("itemID")]
    [StringLength(50)]
    public string ItemId { get; set; } = null!;

    [Column("description")]
    [StringLength(150)]
    public string Description { get; set; } = null!;

    [Column("quantity")]
    public double Quantity { get; set; }

    /// <summary>Valor del ítem en la factura del exterior.</summary>
    [Column("importValue")]
    public double ImportValue { get; set; }

    [Column("apportionPercent")]
    public double ApportionPercent { get; set; }

    [Column("apportion")]
    public double Apportion { get; set; }

    /// <summary>Sin uso (el `.exe` guardaba 0).</summary>
    [Column("amountCFR")]
    public double AmountCfr { get; set; }

    /// <summary>Sin uso (el `.exe` guardaba 0).</summary>
    [Column("apportionCFR")]
    public double ApportionCfr { get; set; }

    [Column("apportionCFRPercent")]
    [StringLength(10)]
    public string? ApportionCfrPercent { get; set; }
}

/// <summary>Gasto de una liquidación, o la factura del exterior (<see cref="IsCost"/> = false) (tabla <c>ImportCostEx</c>).</summary>
[Table("ImportCostEx")]
public partial class ImportCostEx
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("importCost")]
    public int ImportCostId { get; set; }

    [Column("date")]
    public DateTime Date { get; set; }

    [Column("vendorName")]
    [StringLength(90)]
    public string VendorName { get; set; } = null!;

    [Column("reference")]
    [StringLength(50)]
    public string Reference { get; set; } = null!;

    [Column("description")]
    [StringLength(160)]
    public string Description { get; set; } = null!;

    [Column("expenseValue")]
    public double ExpenseValue { get; set; }

    [Column("isCost")]
    public bool IsCost { get; set; }

    [Column("costExType")]
    [StringLength(2)]
    public string? CostExType { get; set; }
}
