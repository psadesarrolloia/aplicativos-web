namespace PsaWeb.Modules.Kardex.Data;

/// <summary>
/// Resuelve qué ítems entran al reporte según el <see cref="FiltroKardex"/>.
/// Misma precedencia que la pantalla de escritorio: si hay ítems elegidos mandan
/// ellos; si no, la cuenta de inventario; si no, el rango de ItemID; si no hay
/// nada, no se resuelve nada (el llamador ya exige <c>TieneAcotador</c>).
/// </summary>
internal static class SeleccionItems
{
    public static IReadOnlyList<ItemStock> Filtrar(IEnumerable<ItemStock> items, FiltroKardex filtro)
    {
        if (filtro.ItemIds.Count > 0)
        {
            var set = new HashSet<string>(filtro.ItemIds, StringComparer.OrdinalIgnoreCase);
            return items.Where(i => set.Contains(i.Id)).ToList();
        }

        if (filtro.CuentasGl.Count > 0)
        {
            var cuentas = new HashSet<string>(filtro.CuentasGl, StringComparer.OrdinalIgnoreCase);
            return items.Where(i => cuentas.Contains(i.CuentaGl)).ToList();
        }

        var desde = filtro.ItemDesde?.Trim();
        var hasta = filtro.ItemHasta?.Trim();
        if (string.IsNullOrEmpty(desde) && string.IsNullOrEmpty(hasta))
        {
            return Array.Empty<ItemStock>();
        }

        return items.Where(i =>
                (string.IsNullOrEmpty(desde) || string.Compare(i.Id, desde, StringComparison.OrdinalIgnoreCase) >= 0)
                && (string.IsNullOrEmpty(hasta) || string.Compare(i.Id, hasta, StringComparison.OrdinalIgnoreCase) <= 0))
            .ToList();
    }
}
