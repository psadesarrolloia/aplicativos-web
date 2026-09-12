namespace PsaWeb.Modules.Ats.Data;

/// <summary>
/// Filtro de entrada del ATS: año + mes del período a generar. Misma
/// validación que <c>ATSform</c> del `.exe` (año no puede ser futuro).
/// </summary>
public sealed record FiltroAts(int Anio, int Mes)
{
    public bool MesValido => Mes is >= 1 and <= 12;

    public bool AnioValido => Anio >= 2000 && Anio <= DateTime.Today.Year;

    public bool Valido => MesValido && AnioValido;
}

/// <summary>Fila agregada de compras o ventas por <c>tipoComprobante</c>, para la pestaña "Resumen ATS".</summary>
public sealed record ResumenPorTipoComprobante(
    string TipoComprobante, int Cantidad, decimal BaseNoGraIva, decimal BaseImponible, decimal BaseImpGrav, decimal MontoIva);

/// <summary>Retenciones en la fuente de impuesto a la renta en compras, agrupadas por <c>codRetAir</c>.</summary>
public sealed record ResumenRetencionRentaCompras(string CodRetAir, int Cantidad, decimal BaseImpAir, decimal ValRetAir);

/// <summary>Retención de IVA en compras por porcentaje (10/20/30/50/70/100 %).</summary>
public sealed record ResumenRetencionIvaCompras(string Porcentaje, int Cantidad, decimal Valor);

/// <summary>Retenciones que le efectuaron al informante en sus ventas (recibidas de sus clientes).</summary>
public sealed record ResumenRetencionesRecibidasVentas(decimal TotalIva, decimal TotalRenta);

/// <summary>Conteo de comprobantes anulados por tipo.</summary>
public sealed record ResumenAnulados(string TipoComprobante, int Cantidad);

/// <summary>Los 6 agregados de la pestaña "Resumen ATS" (§1.2 del plan).</summary>
public sealed record ResumenAts(
    IReadOnlyList<ResumenPorTipoComprobante> ComprasPorTipo,
    IReadOnlyList<ResumenPorTipoComprobante> VentasPorTipo,
    IReadOnlyList<ResumenRetencionRentaCompras> RetencionesRentaCompras,
    IReadOnlyList<ResumenRetencionIvaCompras> RetencionesIvaCompras,
    ResumenRetencionesRecibidasVentas RetencionesRecibidasVentas,
    IReadOnlyList<ResumenAnulados> Anulados);
