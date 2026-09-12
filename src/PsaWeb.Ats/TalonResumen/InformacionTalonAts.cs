namespace PsaWeb.Ats.TalonResumen;

/// <summary>Una fila de las tablas COMPRAS/VENTAS del Talón (una por tipoComprobante) o su TOTAL.</summary>
public sealed record FilaTipoComprobante(
    string Codigo, string Transaccion, int NumRegistros,
    decimal BiTarifa0, decimal BiTarifaDiferente0, decimal BiNoObjetoIva, decimal ValorIva);

/// <summary>Una fila de "RETENCION EN LA FUENTE DE IMPUESTO A LA RENTA" o su TOTAL.</summary>
public sealed record FilaRetencionRenta(string Codigo, string Concepto, int NumRegistros, decimal BaseImponible, decimal ValorRetenido);

/// <summary>Una fila de "RETENCION EN LA FUENTE DE IVA" (Operación siempre "COMPRA").</summary>
public sealed record FilaRetencionIva(string Concepto, decimal ValorRetenido);

/// <summary>
/// Todo lo que necesita <see cref="TalonResumenPdfBuilder"/> para armar el PDF,
/// ya calculado por <see cref="ArmadorTalonResumenAts"/>.
/// </summary>
public sealed record InformacionTalonAts(
    string Ruc,
    string RazonSocial,
    string Periodo,
    DateTime FechaGeneracion,
    IReadOnlyList<FilaTipoComprobante> Compras,
    FilaTipoComprobante TotalCompras,
    IReadOnlyList<FilaTipoComprobante> Ventas,
    FilaTipoComprobante TotalVentas,
    int ComprobantesAnulados,
    IReadOnlyList<FilaRetencionRenta> RetencionesRenta,
    FilaRetencionRenta TotalRetencionesRenta,
    IReadOnlyList<FilaRetencionIva> RetencionesIva,
    decimal TotalRetencionesIva,
    decimal RetencionesRecibidasIva,
    decimal RetencionesRecibidasRenta)
{
    public decimal TotalRetencionesRecibidas => RetencionesRecibidasIva + RetencionesRecibidasRenta;
}
