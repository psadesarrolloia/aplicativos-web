using System.Globalization;
using PsaWeb.Ats.Esquema;

namespace PsaWeb.Ats.TalonResumen;

/// <summary>
/// Arma <see cref="InformacionTalonAts"/> a partir del <c>ivaType</c> ya
/// generado — las mismas agregaciones de la pestaña "Resumen ATS" (F5), con
/// el layout exacto del Talón Resumen real del DIMM (§3.5b del plan).
/// Verificado campo por campo contra <c>TRSMN-ATS-07-2026-CPTDC.pdf</c> (el
/// ejemplo real de CPTDC julio/2026): todas las filas y los 4 totales de
/// COMPRAS/VENTAS, las 2 tablas de retenciones y "retenciones recibidas"
/// coinciden exacto. Parte pura, sin ODBC/EF/render.
/// </summary>
public static class ArmadorTalonResumenAts
{
    private static readonly IReadOnlyDictionary<string, string> EtiquetasCompras = new Dictionary<string, string>
    {
        ["01"] = "FACTURA",
        ["02"] = "NOTA DE VENTA",
        ["03"] = "LIQUIDACIÓN DE COMPRA DE BIENES O PRESTACION DE SERVICIOS",
        ["04"] = "NOTAS DE CREDITO",
    };

    private const string EtiquetaVentas = "DOCUMENTOS AUTORIZADOS EN VENTAS EXCEPTO ND Y NC";

    public static InformacionTalonAts Armar(ivaType ats, DateTime fechaGeneracion)
    {
        var compras = ats.compras ?? Array.Empty<detalleComprasType>();
        var ventas = ats.ventas ?? Array.Empty<detalleVentasType>();
        var anulados = ats.anulados ?? Array.Empty<detalleAnuladosType>();

        var filasCompras = compras
            .GroupBy(c => c.tipoComprobante ?? "")
            .Select(g => new FilaTipoComprobante(
                g.Key, EtiquetasCompras.TryGetValue(g.Key, out var t) ? t : g.Key, g.Count(),
                g.Sum(c => c.baseImponible), g.Sum(c => c.baseImpGrav), g.Sum(c => c.baseNoGraIva), g.Sum(c => c.montoIva)))
            .OrderBy(f => f.Codigo, StringComparer.Ordinal)
            .ToList();

        // TOTAL de compras: las Notas de Crédito (04) restan, igual que
        // ArmadorVentasAts.TotalVentas — confirmado contra el Talón real
        // (270741.02+2664.00+0.00-110.00 = 273295.02, la suma cruda sin restar
        // daría 273515.02, que NO es lo que imprime el DIMM).
        var totalCompras = TotalConNcRestando(filasCompras);

        // La tabla VENTAS del Talón excluye NC por completo (título literal:
        // "...EXCEPTO ND Y NC") — no es solo el total, la fila "04" ni
        // aparece.
        var filaVentas18 = ventas.Where(v => v.tipoComprobante == "18").ToList();
        var filasVentas = filaVentas18.Count == 0
            ? new List<FilaTipoComprobante>()
            : new List<FilaTipoComprobante>
            {
                new("18", EtiquetaVentas, filaVentas18.Count,
                    filaVentas18.Sum(v => v.baseImponible), filaVentas18.Sum(v => v.baseImpGrav),
                    filaVentas18.Sum(v => v.baseNoGraIva), filaVentas18.Sum(v => v.montoIva)),
            };
        var totalVentas = filasVentas.Count == 0
            ? new FilaTipoComprobante("", "", 0, 0, 0, 0, 0)
            : filasVentas[0] with { Codigo = "", Transaccion = "" };

        var retencionesRenta = compras
            .SelectMany(c => c.air ?? Array.Empty<detalleAirComprasType>())
            .GroupBy(a => a.codRetAir ?? "")
            .Select(g => new FilaRetencionRenta(
                g.Key, CatalogoRetencionRenta.Buscar(g.Key), g.Count(), g.Sum(a => a.baseImpAir), g.Sum(a => a.valRetAir)))
            .OrderBy(f => f.Codigo, StringComparer.Ordinal)
            .ToList();
        var totalRetencionesRenta = new FilaRetencionRenta(
            "", "", retencionesRenta.Sum(f => f.NumRegistros),
            retencionesRenta.Sum(f => f.BaseImponible), retencionesRenta.Sum(f => f.ValorRetenido));

        var retencionesIva = new List<FilaRetencionIva>
        {
            new("Retencion IVA 10%", compras.Sum(c => c.valRetBien10)),
            new("Retencion IVA 20%", compras.Sum(c => c.valRetServ20)),
            new("Retencion IVA 30%", compras.Sum(c => c.valorRetBienes)),
            new("Retencion IVA 50%", compras.Sum(c => c.valRetServ50)),
            new("Retencion IVA 70%", compras.Sum(c => c.valorRetServicios)),
            new("Retencion IVA 100%", compras.Sum(c => c.valRetServ100)),
            new("Retencion IVA NC", compras.Sum(c => c.valorRetencionNc)),
        };

        return new InformacionTalonAts(
            Ruc: ats.IdInformante,
            RazonSocial: ats.razonSocial,
            Periodo: $"{ats.Mes}-{ats.Anio}",
            FechaGeneracion: fechaGeneracion,
            Compras: filasCompras,
            TotalCompras: totalCompras,
            Ventas: filasVentas,
            TotalVentas: totalVentas,
            ComprobantesAnulados: anulados.Length,
            RetencionesRenta: retencionesRenta,
            TotalRetencionesRenta: totalRetencionesRenta,
            RetencionesIva: retencionesIva,
            TotalRetencionesIva: retencionesIva.Sum(f => f.ValorRetenido),
            RetencionesRecibidasIva: ventas.Sum(v => v.valorRetIva),
            RetencionesRecibidasRenta: ventas.Sum(v => v.valorRetRenta));
    }

    private static FilaTipoComprobante TotalConNcRestando(IReadOnlyList<FilaTipoComprobante> filas)
    {
        decimal Signo(FilaTipoComprobante f) => f.Codigo == "04" ? -1m : 1m;
        return new FilaTipoComprobante(
            "", "", 0,
            filas.Sum(f => Signo(f) * f.BiTarifa0),
            filas.Sum(f => Signo(f) * f.BiTarifaDiferente0),
            filas.Sum(f => Signo(f) * f.BiNoObjetoIva),
            filas.Sum(f => Signo(f) * f.ValorIva));
    }
}
