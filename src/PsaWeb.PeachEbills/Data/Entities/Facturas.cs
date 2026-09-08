using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PsaWeb.PeachEbills.Data;

/// <summary>
/// Cabecera de un comprobante de venta / liquidación ya emitido (tabla
/// <c>Facturas</c> de PeachEBills). También se usa para notas de crédito
/// (con una fila en <see cref="NCdetail"/>).
/// </summary>
public partial class Facturas
{
    [Key]
    public int FacturaId { get; set; }

    /// <summary>01 factura, 03 liquidación, 04 nota de crédito.</summary>
    [Column("codDoc", TypeName = "char")]
    [StringLength(2)]
    public string CodDoc { get; set; } = null!;

    [StringLength(20)]
    public string FacturaNumberComplete { get; set; } = null!;

    [StringLength(9)]
    public string FacturaNumber { get; set; } = null!;

    public int? TransmitterEstablishment { get; set; }

    [StringLength(13)]
    public string? TransmitterRuc { get; set; }

    [Column(TypeName = "char")]
    [StringLength(3)]
    public string CurrencyIsoId { get; set; } = "USD";

    [Column(TypeName = "datetime")]
    public DateTime DateIssued { get; set; }

    public short Ambient { get; set; }

    [Column("comprador")]
    [StringLength(20)]
    public string Comprador { get; set; } = null!;

    public short IssueType { get; set; } = 1;

    public double TotalBillTip { get; set; }

    public double TotalDiscount { get; set; }

    [Column(TypeName = "char")]
    [StringLength(1)]
    public string IVACode { get; set; } = "2";

    [Column(TypeName = "char")]
    [StringLength(1)]
    public string PercentIVACode { get; set; } = "2";

    public double TotalWithoutTax { get; set; }

    public double TotalAmountForIVA { get; set; }

    public double IVAValue { get; set; }

    public double TotalAmount { get; set; }

    [Column("DatilID")]
    public string? DatilId { get; set; }

    [StringLength(100)]
    public string? PostOrderPeach { get; set; }

    public bool IsValid { get; set; } = true;

    [Column(TypeName = "datetime")]
    public DateTime? ChangeIsValidDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateDue { get; set; }

    public int TransType { get; set; } = 1;

    public bool TwhRequired { get; set; }

    public int? AboutApplyTwh { get; set; }

    [StringLength(50)]
    public string? BillTipAccItemID { get; set; }

    [StringLength(50)]
    public string? BillTipAccAccountID { get; set; }
}
