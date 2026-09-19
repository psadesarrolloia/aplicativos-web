using System.ComponentModel.DataAnnotations;

namespace PsaWeb.Conciliacion.Data;

/// <summary>
/// Solicitud de anulación de un comprobante emitido (factura, retención, nota de crédito o
/// liquidación) y su seguimiento hasta que el SRI la refleje. La anulación real la hace una
/// persona en el portal del SRI (no hay API): la app la solicita, avisa y la <em>rastrea</em>.
/// </summary>
public class SolicitudAnulacionComprobante
{
    public long Id { get; set; }

    [MaxLength(13)]
    public string Ruc { get; set; } = string.Empty;

    /// <summary>Código SRI: 01 factura, 07 retención, 04 nota de crédito, 03 liquidación.</summary>
    [MaxLength(2)]
    public string CodDoc { get; set; } = string.Empty;

    /// <summary>Id en PeachEBills: <c>Facturas.FacturaId</c> o <c>TaxWithHoldings.THId</c>.</summary>
    public int RefId { get; set; }

    /// <summary>Número completo del comprobante (001-001-000000123), para mostrar.</summary>
    [MaxLength(40)]
    public string Numero { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? DatilId { get; set; }

    public DateOnly FechaEmision { get; set; }

    /// <summary>1 = pruebas, 2 = producción.</summary>
    public short Ambiente { get; set; }

    [MaxLength(100)]
    public string SolicitadaPor { get; set; } = string.Empty;

    /// <summary>UTC. Es el inicio del conteo de días del seguimiento.</summary>
    public DateTime FechaSolicitud { get; set; }

    /// <summary><see cref="EstadoAnulacion"/> como texto: Solicitada, Anulada, SinEfecto, Cancelada.</summary>
    [MaxLength(20)]
    public string Estado { get; set; } = EstadoAnulacion.Solicitada;

    /// <summary>UTC. Cuando el seguimiento se cerró (Anulada, SinEfecto o Cancelada).</summary>
    public DateTime? FechaResolucion { get; set; }

    /// <summary>Quién/qué cerró el seguimiento: un usuario o <c>worker</c>.</summary>
    [MaxLength(100)]
    public string? ResueltaPor { get; set; }

    /// <summary>Último estado del SRI visto durante el seguimiento (Autorizado, Anulado, …).</summary>
    [MaxLength(30)]
    public string? UltimoEstadoSri { get; set; }

    public DateTime? FechaUltimaVerificacion { get; set; }

    [MaxLength(500)]
    public string? Detalle { get; set; }
}

/// <summary>Estados del seguimiento de una solicitud de anulación.</summary>
public static class EstadoAnulacion
{
    /// <summary>Pedida; el SRI todavía la muestra vigente. Es el único estado «abierto».</summary>
    public const string Solicitada = "Solicitada";

    /// <summary>El SRI confirmó <c>ANULADO</c>.</summary>
    public const string Anulada = "Anulada";

    /// <summary>
    /// Retenciones/notas de crédito: pasaron 5 días y el SRI sigue mostrándolo autorizado (el receptor
    /// no aceptó, o la solicitud no llegó a presentarse en el SRI). Se cierra el seguimiento.
    /// </summary>
    public const string SinEfecto = "SinEfecto";

    /// <summary>Un usuario con permiso de autorizar anulaciones retiró la solicitud.</summary>
    public const string Cancelada = "Cancelada";
}
