namespace PsaWeb.Modules.Ventas.Data;

/// <summary>Datos de muestra (sin tocar Sage 50) para desarrollo sin cadena de conexión.</summary>
internal sealed class SampleVentasRepository : IVentasRepository
{
    private static readonly IReadOnlyList<ItemVenta> Items = new[]
    {
        Item("CA-003", "CABLE THHN 12 AWG NEGRO", "CABLES", 26000m, 0.42m, 0.45m, 0.48m),
        Item("BR-2049", "BREAKER 3 POLOS 320AMP 415VAC", "SIE-BCM", 12m, 310m, 325m, 340m),
        Item("BR-0060", "BREAKER 3 POLOS 400-1000 AMP", "SIE-BCM", 0m, 980m, 1020m, 1065m),
        Item("TE-2250", "TABLERO DE DISTRIBUCION (TD-CAMARAS)", "TE", 0m, 1450m, 1500m, 0m, ensamblado: true),
    };

    private static readonly IReadOnlyList<ClienteVenta> Clientes = new[]
    {
        new ClienteVenta("CLIENTE DEMO", "CLIENTE DEMO S.A.", "Ana Pérez", "0999999999", "demo@example.com", 0, 30, 5000m, 1200m, "VENDEDOR UNO"),
        new ClienteVenta("CLIENTE NIVEL 2", "CLIENTE NIVEL 2 CIA. LTDA.", "Luis Mora", "0988888888", "nivel2@example.com", 1, 1, 0m, 0m, "VENDEDOR DOS"),
    };

    public Task<IReadOnlyList<string>> CategoriasAsync(bool incluirEnsamblados, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Items.Where(i => incluirEnsamblados || !i.EsEnsamblado).Select(i => i.Categoria).Distinct().Order().ToList());

    public Task<IReadOnlyList<ItemVenta>> BuscarItemsAsync(FiltroItems filtro, CancellationToken cancellationToken = default)
    {
        var terminos = OdbcVentasRepository.Terminos(filtro.Texto);
        var q = Items.Where(i => (filtro.IncluirEnsamblados || !i.EsEnsamblado)
                                 && (string.IsNullOrWhiteSpace(filtro.Categoria) || i.Categoria == filtro.Categoria)
                                 && (!filtro.SoloConExistencia || i.Existencia > 0)
                                 && terminos.All(t => i.Id.ToUpperInvariant().Contains(t) || i.Descripcion.ToUpperInvariant().Contains(t)));
        return Task.FromResult<IReadOnlyList<ItemVenta>>(q.Take(filtro.Maximo).ToList());
    }

    public Task<IReadOnlyList<ClienteVenta>> BuscarClientesAsync(string texto, int maximo = 20, CancellationToken cancellationToken = default)
    {
        var terminos = OdbcVentasRepository.Terminos(texto);
        return Task.FromResult<IReadOnlyList<ClienteVenta>>(Clientes
            .Where(c => terminos.All(t => c.Id.ToUpperInvariant().Contains(t) || c.Nombre.ToUpperInvariant().Contains(t))).Take(maximo).ToList());
    }

    private static ItemVenta Item(string id, string descripcion, string categoria, decimal existencia, decimal l1, decimal l2, decimal l3, bool ensamblado = false)
    {
        var precios = new decimal[PreciosDeVenta.NivelesMaximos];
        precios[0] = l1;
        precios[1] = l2;
        precios[2] = l3;
        return new ItemVenta(id, descripcion, categoria, ensamblado, "UND", existencia, precios);
    }
}
