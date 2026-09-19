using System.ComponentModel.DataAnnotations;

namespace PsaWeb.Conciliacion.Data;

/// <summary>
/// Estado en el SRI de un comprobante <em>emitido</em> por nosotros (factura, retención,
/// nota de crédito o liquidación). Vive junto al staging de Conciliación en
/// <c>PsaWebPlataforma</c>: no se agregan columnas a <c>PeachEBills</c> porque los
/// <c>.exe</c> de escritorio la usan. Una fila por comprobante
/// (<see cref="Ruc"/>, <see cref="CodDoc"/>, <see cref="RefId"/>).
/// </summary>
public class EstadoSriComprobante
{
    public long Id { get; set; }

    [MaxLength(13)]
    public string Ruc { get; set; } = string.Empty;

    /// <summary>Código SRI del tipo: 01 factura, 07 retención, 04 nota de crédito, 03 liquidación.</summary>
    [MaxLength(2)]
    public string CodDoc { get; set; } = string.Empty;

    /// <summary>Id del comprobante en PeachEBills: <c>Facturas.FacturaId</c> o <c>TaxWithHoldings.THId</c>.</summary>
    public int RefId { get; set; }

    /// <summary>Clave de acceso de 49 dígitos, si ya se pudo recuperar (DatilRequests o Datil).</summary>
    [MaxLength(49)]
    public string? ClaveAcceso { get; set; }

    public DateOnly FechaEmision { get; set; }

    /// <summary>Nombre de <see cref="EstadoComprobanteSri"/> (Autorizado, Anulado, …); null = sin verificar.</summary>
    [MaxLength(30)]
    public string? Estado { get; set; }

    /// <summary>De dónde salió el estado: <c>SRI</c> (WS público) o <c>Datil</c>.</summary>
    [MaxLength(10)]
    public string? Fuente { get; set; }

    /// <summary>Texto del SRI/Datil o motivo por el que no se pudo verificar.</summary>
    [MaxLength(500)]
    public string? Detalle { get; set; }

    /// <summary>Respuesta cruda (para no perder estados que todavía no conocemos).</summary>
    public string? RespuestaCruda { get; set; }

    /// <summary>UTC de la última verificación exitosa.</summary>
    public DateTime? FechaVerificacion { get; set; }
}
