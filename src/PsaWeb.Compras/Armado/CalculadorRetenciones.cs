using PsaWeb.Compras.Catalogo;

namespace PsaWeb.Compras.Armado;

/// <summary>
/// Grilla de retenciones (port de <c>Services.TwhApply.CheckTwhApply</c> + el filtro de <c>Bill.LoadTwhItems</c>).
/// </summary>
public static class CalculadorRetenciones
{
    /// <summary>Código de porcentaje de las retenciones de seguros: se retiene sobre el 10 % de la base.</summary>
    public const string CodigoSeguros = "322";

    /// <summary>
    /// Renta: una línea por ítem R-IRF, base = suma de las líneas que lo llevan (322: el 10 %). IVA: una línea por
    /// (ítem R-IVA, tarifa), base = suma × tarifa redondeada lejos de cero. Con retención asumida (forma de pago 7) cada
    /// línea lleva la cuenta de gasto del proveedor como contrapartida.
    /// </summary>
    /// <returns><c>null</c> si ninguna línea lleva retención (el `.exe` no llena la grilla y la OC va sin nº de retención).</returns>
    public static List<LineaRetencion>? Calcular(
        IReadOnlyList<LineaDetalle> detalles, int formaPagoId, string? cuentaGastoProveedor, CatalogoCompras catalogo)
    {
        var conFuente = detalles.Where(x => x.RetencionFuenteId is not null).ToList();
        var conIva = detalles.Where(x => x.RetencionIvaId is not null).ToList();
        if (conFuente.Count == 0 && conIva.Count == 0) return null;

        var asumida = formaPagoId == FormaPagoRetencion.RetencionAsumida && !string.IsNullOrEmpty(cuentaGastoProveedor)
            ? cuentaGastoProveedor! : string.Empty;
        var lineas = new List<LineaRetencion>();

        foreach (var g in conFuente.GroupBy(x => x.RetencionFuenteId!))
        {
            var item = catalogo.Retencion(g.Key);
            var baseImponible = g.Sum(x => x.MontoSinImpuestos);
            var porcentaje = item?.PorcentajeRetencion ?? 0m;
            var valor = Math.Round(baseImponible * porcentaje, 2);
            if (item?.CustomField1 == CodigoSeguros)
            {
                baseImponible *= 0.1m;
                valor = Math.Round(valor * 0.1m, 2);
            }
            lineas.Add(new LineaRetencion(g.Key, item?.CodigoImpuestoRetencion ?? string.Empty, item?.CuentaInventario,
                baseImponible, valor, asumida));
        }

        foreach (var g in conIva.GroupBy(x => (x.RetencionIvaId!, x.TarifaIva)))
        {
            var item = catalogo.Retencion(g.Key.Item1);
            var tasa = g.Key.TarifaIva >= 1 ? g.Key.TarifaIva / 100m : g.Key.TarifaIva;
            var baseImponible = Math.Round(g.Sum(x => x.MontoSinImpuestos) * tasa, 2, MidpointRounding.AwayFromZero);
            var valor = Math.Round(baseImponible * (item?.PorcentajeRetencion ?? 0m), 2);
            lineas.Add(new LineaRetencion(g.Key.Item1, item?.CodigoImpuestoRetencion ?? string.Empty, item?.CuentaInventario,
                baseImponible, valor, asumida));
        }

        // LoadTwhItems: se descartan las filas con base y valor en cero (p. ej. líneas sin monto).
        return lineas.Where(x => x.Valor != 0 || x.BaseImponible != 0).ToList();
    }
}
