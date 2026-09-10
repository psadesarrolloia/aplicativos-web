namespace PsaWeb.Modules.Kardex.Data;

/// <summary>
/// Acceso de solo lectura al kardex de inventarios de Sage 50. Reproduce lo que
/// hace la pantalla «Reporte de Stock» (<c>StockConfig</c>) del monolito
/// <c>Sage50usIntegration</c>: lista de ítems stock, cuentas de inventario y el
/// kardex por ítem (fila inicial + movimientos + saldo corrido).
/// </summary>
public interface IKardexRepository
{
    /// <summary>
    /// Ítems de inventario (stock) de la empresa de sesión, para el selector.
    /// </summary>
    Task<IReadOnlyList<ItemStock>> ItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cuentas contables de inventario de la empresa, para el filtro «por cuenta».
    /// </summary>
    Task<IReadOnlyList<CuentaInventario>> CuentasAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Genera el kardex para <paramref name="filtro"/> contra la empresa de
    /// sesión (o la de configuración si no hay shell).
    /// </summary>
    Task<ResultadoKardex> GenerarAsync(FiltroKardex filtro, CancellationToken cancellationToken = default);

    /// <summary>
    /// Igual que <see cref="GenerarAsync"/> pero contra una empresa explícita por
    /// RUC. Lo usará el endpoint de exportación (F3), que no tiene el contexto del
    /// circuito Blazor.
    /// </summary>
    Task<ResultadoKardex> GenerarParaRucAsync(
        string ruc, FiltroKardex filtro, CancellationToken cancellationToken = default);
}
