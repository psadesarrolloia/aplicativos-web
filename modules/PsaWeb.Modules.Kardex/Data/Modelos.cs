namespace PsaWeb.Modules.Kardex.Data;

/// <summary>
/// Un bloque de valores del kardex: cantidad, costo unitario y costo total.
/// Se usa para «Entradas», «Salidas» y «Saldos». Los tres nulos = sin dato
/// (p. ej. una fila de salida no tiene bloque de entrada).
/// </summary>
public sealed record MovimientoKardex(decimal? Cantidad, decimal? CostoUnitario, decimal? CostoTotal)
{
    public static readonly MovimientoKardex Vacio = new(null, null, null);

    /// <summary>true si el bloque tiene cantidad (se pintó un movimiento).</summary>
    public bool TieneValor => Cantidad is not null;
}

/// <summary>
/// Una fila del kardex de un ítem: la fila inicial (<see cref="EsInicial"/>) con
/// el saldo de apertura, o una fila de movimiento (compra / venta / ajuste).
/// </summary>
public sealed record FilaKardex(
    string CuentaGl,
    string ItemId,
    string Nombre,
    string Categoria,
    DateOnly Fecha,
    string Referencia,
    MovimientoKardex Entrada,
    MovimientoKardex Salida,
    MovimientoKardex Saldo,
    bool EsInicial);

/// <summary>Un ítem de inventario (stock) elegible para el reporte.</summary>
public sealed record ItemStock(string Id, string Nombre, string Categoria, string CuentaGl);

/// <summary>Una cuenta contable de inventario, para el filtro «por cuenta».</summary>
public sealed record CuentaInventario(string Id, string Descripcion);

/// <summary>
/// Filtro para generar el kardex. Exige al menos un acotador (ítems, cuenta o
/// rango de ItemID): la empresa puede tener miles de ítems stock y «todos» no es
/// una opción razonable en web.
/// </summary>
public sealed record FiltroKardex(
    DateOnly Desde,
    DateOnly Hasta,
    IReadOnlyList<string> ItemIds,
    string? CuentaGl,
    string? ItemDesde,
    string? ItemHasta,
    bool IncluirVacios = true)
{
    /// <summary>true si hay al menos un acotador definido.</summary>
    public bool TieneAcotador =>
        ItemIds.Count > 0
        || !string.IsNullOrWhiteSpace(CuentaGl)
        || !string.IsNullOrWhiteSpace(ItemDesde)
        || !string.IsNullOrWhiteSpace(ItemHasta);

    /// <summary>true si «Hasta» es posterior a «Desde» (mismo criterio que el .exe).</summary>
    public bool RangoValido => Hasta > Desde;
}

/// <summary>Resultado del kardex para un filtro.</summary>
public sealed record ResultadoKardex(IReadOnlyList<FilaKardex> Filas)
{
    public static readonly ResultadoKardex Vacio = new(Array.Empty<FilaKardex>());

    public bool SinMovimientos => Filas.Count == 0;
}
