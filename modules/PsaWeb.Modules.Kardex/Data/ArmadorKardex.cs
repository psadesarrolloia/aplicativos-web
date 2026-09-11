namespace PsaWeb.Modules.Kardex.Data;

/// <summary>
/// Una fila cruda de <c>InventoryCosts</c> (más el <c>Reference</c> de
/// <c>JrnlHdr</c> y el <c>ItemID</c> de <c>LineItem</c>). Los montos son
/// anulables: en Sage pueden venir <c>NULL</c> y el original los trata como 0.
/// </summary>
internal sealed record CostoCrudo(
    string ItemId,
    DateTime TransDate,
    string? Reference,
    long PostOrder,
    decimal? TransAmount,
    decimal? Quantity,
    decimal? OptAmount,
    long MajorType);

/// <summary>
/// Port fiel del cuerpo de <c>InventroyCostQuery</c> (pantalla «Reporte de Stock»
/// de <c>Sage50usIntegration</c>). Lógica pura y testeable: recibe los ítems
/// elegidos y las tres listas crudas (saldos iniciales, movimientos, saldos por
/// movimiento) y arma las filas del kardex.
///
/// <para><b>MajorType</b>: 1 = compra, 2 = venta, 3 = saldo.</para>
///
/// <para>Se conservan los comportamientos del .exe, incluidos los discutibles:</para>
/// <list type="bullet">
///   <item><b>B1</b>: la fila <c>.INICIAL.</c> calcula el costo unitario como
///   <c>TransAmount / Quantity</c>, ignorando <c>OptAmount</c>.</item>
///   <item><b>B2</b>: si un ítem no tiene fila de saldo (MajorType 3) previa a
///   «Desde», no se emite <c>.INICIAL.</c> y el saldo corrido del Excel arranca
///   desde el primer movimiento.</item>
///   <item><b>B4</b>: en compra, si <c>OptAmount == TransAmount</c> se recalcula
///   el unitario; en venta, si <c>OptAmount == 0</c> se recalcula.</item>
///   <item>El bloque «Saldos» de las filas de movimiento sale del snapshot
///   MajorType 3 de Sage <b>sin</b> recálculo (el Excel, en F3, lo recalcula por
///   acumulación con fórmulas: ahí puede diferir de este valor — decisión 4 del
///   plan).</item>
/// </list>
/// </summary>
internal static class ArmadorKardex
{
    private const long Compra = 1;
    private const long Venta = 2;

    public static IReadOnlyList<FilaKardex> Armar(
        IReadOnlyList<ItemStock> itemsElegidos,
        IEnumerable<CostoCrudo> saldosIniciales,
        IEnumerable<CostoCrudo> movimientos,
        IEnumerable<CostoCrudo> saldosPorMovimiento,
        DateOnly desde,
        bool incluirVacios = true)
    {
        var iniPorItem = saldosIniciales
            .GroupBy(x => x.ItemId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.TransDate).First(), StringComparer.OrdinalIgnoreCase);

        // El .exe ordena por (fecha, tipo). `PostOrder` como último desempate hace
        // el reporte determinista cuando hay varios movimientos del mismo tipo el
        // mismo día (el .exe ahí depende del orden físico que devuelva Pervasive).
        var movPorItem = movimientos
            .Where(x => x.MajorType != 3)
            .GroupBy(x => x.ItemId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CostoCrudo>)g
                .OrderBy(x => x.TransDate).ThenBy(x => x.MajorType).ThenBy(x => x.PostOrder).ToList(),
                StringComparer.OrdinalIgnoreCase);

        // Snapshot de saldo por (ItemID, PostOrder). Si hay más de uno, el .exe
        // toma el primero por (ItemID, TransDate, MajorType).
        var salPorClave = saldosPorMovimiento
            .GroupBy(x => (x.ItemId.ToUpperInvariant(), x.PostOrder))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.TransDate).ThenBy(x => x.MajorType).First());

        var filas = new List<FilaKardex>();

        foreach (var item in itemsElegidos)
        {
            var delItem = new List<FilaKardex>();

            if (iniPorItem.TryGetValue(item.Id, out var ini))
            {
                var qu = ini.Quantity ?? 0m;
                var total = ini.TransAmount ?? 0m;
                var u = qu == 0m ? 0m : total / qu; // B1
                delItem.Add(new FilaKardex(
                    item.CuentaGl, item.Id, item.Nombre, item.Categoria,
                    desde, ".INICIAL.",
                    MovimientoKardex.Vacio, MovimientoKardex.Vacio,
                    new MovimientoKardex(qu, u, total),
                    EsInicial: true));
            }

            if (!movPorItem.TryGetValue(item.Id, out var movs))
            {
                if (incluirVacios || !EsSoloInicialEnCero(delItem))
                {
                    filas.AddRange(delItem);
                }
                continue;
            }

            foreach (var mov in movs)
            {
                // El .exe solo pinta compras y ventas; cualquier otro MajorType se ignora.
                if (mov.MajorType != Compra && mov.MajorType != Venta)
                {
                    continue;
                }

                var qu = mov.Quantity ?? 0m;
                var u = mov.OptAmount ?? 0m;
                var total = mov.TransAmount ?? 0m;

                if (mov.MajorType == Compra && u == total)
                {
                    u = qu == 0m ? 0m : total / qu; // B4
                }
                else if (mov.MajorType == Venta && u == 0m)
                {
                    u = qu == 0m ? 0m : total / qu; // B4
                }

                var movimiento = new MovimientoKardex(qu, u, total);

                MovimientoKardex saldo = MovimientoKardex.Vacio;
                if (salPorClave.TryGetValue((item.Id.ToUpperInvariant(), mov.PostOrder), out var bal))
                {
                    saldo = new MovimientoKardex(bal.Quantity ?? 0m, bal.OptAmount ?? 0m, bal.TransAmount ?? 0m);
                }
                else
                {
                    saldo = new MovimientoKardex(0m, 0m, 0m);
                }

                var esCompra = mov.MajorType == Compra;
                delItem.Add(new FilaKardex(
                    item.CuentaGl, item.Id, item.Nombre, item.Categoria,
                    DateOnly.FromDateTime(mov.TransDate),
                    mov.Reference ?? string.Empty,
                    esCompra ? movimiento : MovimientoKardex.Vacio,
                    esCompra ? MovimientoKardex.Vacio : movimiento,
                    saldo,
                    EsInicial: false));
            }

            // Con movimientos siempre entra; el filtro sólo descarta ítems cuya
            // única fila es un `.INICIAL.` con saldo en cero.
            filas.AddRange(delItem);
        }

        return filas;
    }

    /// <summary>
    /// true si el ítem no aporta información: su única fila es un <c>.INICIAL.</c>
    /// con saldo (cantidad, costo unit. y costo total) en cero. Es el «ruido» que
    /// el check «incluir ítems con saldo inicial 0 y sin movimientos» filtra.
    /// </summary>
    internal static bool EsSoloInicialEnCero(IReadOnlyList<FilaKardex> filasDeItem)
        => filasDeItem.Count == 1
           && filasDeItem[0].EsInicial
           && (filasDeItem[0].Saldo.Cantidad ?? 0m) == 0m
           && (filasDeItem[0].Saldo.CostoUnitario ?? 0m) == 0m
           && (filasDeItem[0].Saldo.CostoTotal ?? 0m) == 0m;
}
