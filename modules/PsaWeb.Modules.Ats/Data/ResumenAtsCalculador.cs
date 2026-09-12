using PsaWeb.Ats.Esquema;

namespace PsaWeb.Modules.Ats.Data;

/// <summary>
/// Arma los 6 agregados de la pestaña "Resumen ATS" (§1.2 del plan) a partir
/// del <c>ivaType</c> ya generado. Parte pura, sin ODBC/EF — solo LINQ en
/// memoria sobre lo que ya trae <c>ArmadorAts</c>, igual que hace <c>ATSform</c>
/// del `.exe` al pintar esa pestaña.
/// </summary>
internal static class ResumenAtsCalculador
{
    public static ResumenAts Calcular(ivaType ats)
    {
        var compras = ats.compras ?? Array.Empty<detalleComprasType>();
        var ventas = ats.ventas ?? Array.Empty<detalleVentasType>();
        var anulados = ats.anulados ?? Array.Empty<detalleAnuladosType>();

        return new ResumenAts(
            ComprasPorTipo: PorTipoComprobante(
                compras, c => c.tipoComprobante, c => c.baseNoGraIva, c => c.baseImponible, c => c.baseImpGrav, c => c.montoIva),
            VentasPorTipo: PorTipoComprobante(
                ventas, v => v.tipoComprobante, v => v.baseNoGraIva, v => v.baseImponible, v => v.baseImpGrav, v => v.montoIva),
            RetencionesRentaCompras: RetencionesRenta(compras),
            RetencionesIvaCompras: RetencionesIva(compras),
            RetencionesRecibidasVentas: new ResumenRetencionesRecibidasVentas(
                TotalIva: ventas.Sum(v => v.valorRetIva),
                TotalRenta: ventas.Sum(v => v.valorRetRenta)),
            Anulados: anulados
                .GroupBy(a => a.tipoComprobante)
                .Select(g => new ResumenAnulados(g.Key, g.Count()))
                .OrderBy(r => r.TipoComprobante, StringComparer.Ordinal)
                .ToList());
    }

    private static List<ResumenPorTipoComprobante> PorTipoComprobante<T>(
        IReadOnlyList<T> filas,
        Func<T, string> tipoComprobante,
        Func<T, decimal> baseNoGraIva,
        Func<T, decimal> baseImponible,
        Func<T, decimal> baseImpGrav,
        Func<T, decimal> montoIva) =>
        filas
            .GroupBy(tipoComprobante)
            .Select(g => new ResumenPorTipoComprobante(
                g.Key, g.Count(), g.Sum(baseNoGraIva), g.Sum(baseImponible), g.Sum(baseImpGrav), g.Sum(montoIva)))
            .OrderBy(r => r.TipoComprobante, StringComparer.Ordinal)
            .ToList();

    private static List<ResumenRetencionRentaCompras> RetencionesRenta(IReadOnlyList<detalleComprasType> compras) =>
        compras
            .SelectMany(c => c.air ?? Array.Empty<detalleAirComprasType>())
            .GroupBy(a => a.codRetAir)
            .Select(g => new ResumenRetencionRentaCompras(g.Key, g.Count(), g.Sum(a => a.baseImpAir), g.Sum(a => a.valRetAir)))
            .OrderBy(r => r.CodRetAir, StringComparer.Ordinal)
            .ToList();

    private static List<ResumenRetencionIvaCompras> RetencionesIva(IReadOnlyList<detalleComprasType> compras)
    {
        (string Porcentaje, Func<detalleComprasType, decimal> Valor)[] buckets =
        [
            ("10%", c => c.valRetBien10),
            ("20%", c => c.valRetServ20),
            ("30%", c => c.valorRetBienes),
            ("50%", c => c.valRetServ50),
            ("70%", c => c.valorRetServicios),
            ("100%", c => c.valRetServ100),
        ];

        return buckets
            .Select(b => new ResumenRetencionIvaCompras(
                b.Porcentaje,
                compras.Count(c => b.Valor(c) != 0m),
                compras.Sum(b.Valor)))
            .ToList();
    }
}
