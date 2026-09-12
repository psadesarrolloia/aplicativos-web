using PsaWeb.Ats.Esquema;
using PsaWeb.Ats.Ventas;

namespace PsaWeb.Ats;

/// <summary>
/// Todo lo ya leído/armado de Sage 50 + PeachEBills para un RUC + período,
/// listo para que <see cref="ArmadorAts"/> arme el <c>ivaType</c> completo.
/// </summary>
/// <param name="EstablecimientosActivos">
/// Códigos de establecimiento activos del RUC (<c>Establishments.Code</c>,
/// deduplicados) — F1 de <c>PsaWeb.PeachEbills</c>. Vacío si la empresa
/// todavía no tiene esa tabla poblada (cae al respaldo "001" del `.exe`).
/// </param>
/// <param name="Ventas">Ya fusionadas por <see cref="ArmadorVentasAts.Fusionar"/>.</param>
/// <param name="VentasPorEstablecimientoCrudo">
/// Sumas reales de Sage por establecimiento (<see cref="Ventas.LectorVentasEstablecimientoAts"/>)
/// — antes de aplicar el fix 1 de docs/PLAN-APP3-ATS.md §1.4.
/// </param>
public sealed record InformacionAts(
    string Ruc,
    string RazonSocial,
    int Anio,
    int Mes,
    IReadOnlyList<string> EstablecimientosActivos,
    IReadOnlyList<detalleVentasType> Ventas,
    IReadOnlyList<ventaEstType> VentasPorEstablecimientoCrudo,
    IReadOnlyList<detalleComprasType> Compras,
    IReadOnlyList<detalleAnuladosType> Anulados);

/// <summary>
/// Arma el <c>ivaType</c> completo del ATS. Parte <b>pura</b> — port de
/// <c>LoadATS.LoadToATSobject</c> (<c>ATSfromPeach</c>), con el fix 1 de
/// docs/PLAN-APP3-ATS.md §1.4 aplicado a <c>numEstabRuc</c>/
/// <c>ventasEstablecimiento</c> (usa los establecimientos activos del RUC,
/// no "los que facturaron el período").
/// </summary>
public static class ArmadorAts
{
    public static ivaType Armar(InformacionAts info)
    {
        var ats = new ivaType
        {
            IdInformante = info.Ruc,
            razonSocial = LimpiarRazonSocial(info.RazonSocial),
            Anio = info.Anio.ToString(),
            Mes = info.Mes.ToString("00"),
        };

        if (info.Ventas.Count > 0)
        {
            ats.ventas = info.Ventas.ToArray();
        }

        ats.totalVentas = ArmadorVentasAts.TotalVentas(info.Ventas);
        ats.totalVentasSpecified = true;

        var establecimientos = ArmarVentasPorEstablecimiento(info.EstablecimientosActivos, info.VentasPorEstablecimientoCrudo);
        ats.numEstabRuc = establecimientos.Count.ToString("000");
        ats.ventasEstablecimiento = establecimientos.ToArray();

        // A diferencia de ventas/anulados, el `.exe` asigna `compras` sin
        // chequear si la lista está vacía — se preserva igual (un
        // <compras></compras> vacío en vez de omitir el elemento).
        ats.compras = info.Compras.ToArray();

        if (info.Anulados.Count > 0)
        {
            ats.anulados = info.Anulados.ToArray();
        }

        return ats;
    }

    /// <summary>
    /// No es cosmético: <c>razonSocialType</c> en el XSD del SRI solo permite
    /// <c>[a-zA-Z0-9\s]</c> — sin este reemplazo el XML no valida. Port de
    /// <c>LoadATS.LoadToATSobject</c>.
    /// </summary>
    public static string LimpiarRazonSocial(string razonSocial) =>
        razonSocial.Replace("&", "y").Replace(".", string.Empty).Replace("-", " ");

    private static List<ventaEstType> ArmarVentasPorEstablecimiento(
        IReadOnlyList<string> establecimientosActivos, IReadOnlyList<ventaEstType> ventasPorEstablecimientoCrudo)
    {
        if (establecimientosActivos.Count == 0)
        {
            // Sin fila en Establishments todavía (RUC recién dado de alta en
            // PeachEBills) — mismo respaldo que el `.exe` cuando no hay ventas:
            // 1 establecimiento "001" en 0.
            return new List<ventaEstType>
            {
                new() { codEstab = "001", ventasEstab = 0m, ivaComp = 0m, ivaCompSpecified = true },
            };
        }

        var ventasPorCodigo = ventasPorEstablecimientoCrudo.ToDictionary(v => v.codEstab, v => v.ventasEstab);

        return establecimientosActivos
            .Distinct()
            .OrderBy(codigo => codigo, StringComparer.Ordinal)
            .Select(codigo => new ventaEstType
            {
                codEstab = codigo,
                ventasEstab = ventasPorCodigo.TryGetValue(codigo, out var monto) ? monto : 0m,
                ivaComp = 0m,
                ivaCompSpecified = true,
            })
            .ToList();
    }
}
