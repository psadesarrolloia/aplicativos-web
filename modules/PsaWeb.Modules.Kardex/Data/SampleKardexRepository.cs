namespace PsaWeb.Modules.Kardex.Data;

/// <summary>
/// Datos ficticios para desarrollo sin tocar Sage 50 (p. ej. en PREDATOR contra
/// el DSN de ejemplo, que no tiene las tablas reales). Deja la pantalla 100%
/// funcional: un par de ítems «casing», dos cuentas de inventario y un kardex
/// con fila inicial + compras + ventas + saldo corrido.
/// </summary>
internal sealed class SampleKardexRepository : IKardexRepository
{
    private static readonly IReadOnlyList<CuentaInventario> Cuentas = new[]
    {
        new CuentaInventario("13101", "INVENTARIOS CASING"),
        new CuentaInventario("13103", "INVENT WELLHEADS"),
    };

    private static readonly IReadOnlyList<ItemStock> Items = new[]
    {
        new ItemStock("CS-001", "Casing 20\", 94 ppf, K55, PE, R3", "CASING", "13101"),
        new ItemStock("CS-012", "Casing 9 5/8\", 47 ppf, N80, BTC", "CASING", "13101"),
        new ItemStock("CS-020", "Casing 7\", 26 ppf, L80, BTC", "CASING", "13101"),
        // Ítem "vacío": sólo un `.INICIAL.` en cero, sin movimientos. Sirve para
        // probar el check «incluir ítems con saldo inicial 0 y sin movimientos».
        new ItemStock("CS-099", "Casing 7\", 26 ppf, sin stock", "CASING", "13101"),
        new ItemStock("CW-001", "Wellhead A-Section 13 3/8\" x 13 5/8\"", "WELLHEAD", "13103"),
    };

    public Task<IReadOnlyList<ItemStock>> ItemsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Items);

    public Task<IReadOnlyList<CuentaInventario>> CuentasAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Cuentas);

    public Task<ResultadoKardex> GenerarAsync(FiltroKardex filtro, CancellationToken cancellationToken = default)
    {
        if (!filtro.RangoValido || !filtro.TieneAcotador)
        {
            return Task.FromResult(ResultadoKardex.Vacio);
        }

        var elegidos = SeleccionItems.Filtrar(Items, filtro);
        if (elegidos.Count == 0)
        {
            return Task.FromResult(ResultadoKardex.Vacio);
        }

        var filas = new List<FilaKardex>();
        foreach (var item in elegidos)
        {
            var delItem = KardexDeMuestra(item, filtro.Desde).ToList();
            if (filtro.IncluirVacios || !ArmadorKardex.EsSoloInicialEnCero(delItem))
            {
                filas.AddRange(delItem);
            }
        }

        return Task.FromResult(new ResultadoKardex(filas));
    }

    public Task<ResultadoKardex> GenerarParaRucAsync(
        string ruc, FiltroKardex filtro, CancellationToken cancellationToken = default)
        => GenerarAsync(filtro, cancellationToken);

    private static IEnumerable<FilaKardex> KardexDeMuestra(ItemStock item, DateOnly desde)
    {
        const decimal costoU = 29.91m;

        // Ítem sin stock ni movimientos: sólo el `.INICIAL.` en cero.
        if (item.Id == "CS-099")
        {
            yield return new FilaKardex(
                item.CuentaGl, item.Id, item.Nombre, item.Categoria,
                desde, ".INICIAL.",
                MovimientoKardex.Vacio, MovimientoKardex.Vacio,
                new MovimientoKardex(0m, 0m, 0m),
                EsInicial: true);
            yield break;
        }

        decimal saldoCant = 1_200m;
        decimal SaldoTotal() => Math.Round(saldoCant * costoU, 2);

        // Fila inicial: solo bloque de saldos.
        yield return new FilaKardex(
            item.CuentaGl, item.Id, item.Nombre, item.Categoria,
            desde, ".INICIAL.",
            MovimientoKardex.Vacio, MovimientoKardex.Vacio,
            new MovimientoKardex(saldoCant, costoU, SaldoTotal()),
            EsInicial: true);

        // Compra.
        var fCompra = desde.AddDays(3);
        var entraCant = 500m;
        saldoCant += entraCant;
        yield return new FilaKardex(
            item.CuentaGl, item.Id, item.Nombre, item.Categoria,
            fCompra, "LIQ IMPORT 007-2026",
            new MovimientoKardex(entraCant, costoU, Math.Round(entraCant * costoU, 2)),
            MovimientoKardex.Vacio,
            new MovimientoKardex(saldoCant, costoU, SaldoTotal()),
            EsInicial: false);

        // Venta (cantidad negativa, como en InventoryCosts de Sage).
        var fVenta = desde.AddDays(6);
        var saleCant = 320m;
        saldoCant -= saleCant;
        yield return new FilaKardex(
            item.CuentaGl, item.Id, item.Nombre, item.Categoria,
            fVenta, "001-001-000003632",
            MovimientoKardex.Vacio,
            new MovimientoKardex(-saleCant, costoU, Math.Round(-saleCant * costoU, 2)),
            new MovimientoKardex(saldoCant, costoU, SaldoTotal()),
            EsInicial: false);
    }
}
