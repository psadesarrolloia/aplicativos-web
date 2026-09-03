using PsaWeb.Comprobantes.Proveedores;
using PsaWeb.Datil.Model;

namespace PsaWeb.Comprobantes.Retenciones;

/// <summary>Datos del emisor (empresa) para armar la retención. Vienen de <c>Transmitter</c>.</summary>
public sealed record EmpresaEmisora(
    string Ruc,
    string RazonSocial,
    string NombreComercial,
    string Direccion,
    string ContribuyenteEspecial,
    bool ObligadoContabilidad);

/// <summary>Establecimiento resuelto (de la tabla <c>Establishments</c>) por código y punto de emisión.</summary>
public sealed record EstablecimientoInfo(
    int EstablishmentId,
    string Codigo,
    string PuntoEmision,
    string Direccion);

/// <summary>Una línea cruda de retención leída de <c>JrnlRow</c> + <c>LineItem</c>.</summary>
public sealed record LineaRetencionCruda(
    string Category,
    string CustomField1,
    string CustomField2,
    decimal Amount,
    decimal Quantity,
    string ItemId);

/// <summary>Cabecera + líneas de la compra leídas de Sage 50 para armar la retención.</summary>
public sealed record DatosCompraRetencion
{
    public string NumeroRetencion { get; init; } = string.Empty;
    public string ShipVia { get; init; } = string.Empty;
    public string NumeroFactura { get; init; } = string.Empty;
    public DateTime FechaCompra { get; init; }
    public DateTime? GoodThruDate { get; init; }
    public string PostOrderPeach { get; init; } = string.Empty;
    public IReadOnlyList<LineaRetencionCruda> Lineas { get; init; } = Array.Empty<LineaRetencionCruda>();
}

/// <summary>Resultado de construir una retención: el comprobante para Datil y/o los errores.</summary>
public sealed class ResultadoRetencion
{
    public Retencion? Retencion { get; init; }
    public string NumeroRetencion { get; init; } = string.Empty;
    public string PostOrderPeach { get; init; } = string.Empty;
    public string Secuencial { get; init; } = string.Empty;
    public string PeriodoFiscal { get; init; } = string.Empty;
    public DateTime FechaEmision { get; init; }
    public int? EstablishmentId { get; init; }
    public string ContactoId { get; init; } = string.Empty;
    public double TotalRetenido { get; init; }
    public IReadOnlyList<string> Errores { get; init; } = Array.Empty<string>();

    public bool Ok => Errores.Count == 0;
}
