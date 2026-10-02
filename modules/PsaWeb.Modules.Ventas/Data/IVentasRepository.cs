namespace PsaWeb.Modules.Ventas.Data;

/// <summary>Lecturas de Sage 50 para el portal de ventas (solo lectura, empresa de sesión).</summary>
public interface IVentasRepository
{
    Task<IReadOnlyList<string>> CategoriasAsync(bool incluirEnsamblados, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ItemVenta>> BuscarItemsAsync(FiltroItems filtro, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClienteVenta>> BuscarClientesAsync(string texto, int maximo = 20, CancellationToken cancellationToken = default);
}
