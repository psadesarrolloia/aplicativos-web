namespace PsaWeb.Modules.Ventas.Data;

/// <summary>Un ítem del catálogo de ventas: lo que el vendedor consulta antes de cotizar o facturar.</summary>
/// <param name="Precios">Precio de cada nivel de Sage: posición 0 = nivel 1 de la lista (<c>PriceLevel1Amount</c>), … posición 9 = nivel 10.</param>
public sealed record ItemVenta(
    string Id,
    string Descripcion,
    string Categoria,
    bool EsEnsamblado,
    string UnidadMedida,
    decimal Existencia,
    IReadOnlyList<decimal> Precios)
{
    /// <summary>Precio de la lista <paramref name="nivelCliente"/> (0-based, como <c>Customers.PriceLevel</c>); <c>null</c> si esa lista no tiene precio.</summary>
    public decimal? PrecioParaNivel(int nivelCliente)
    {
        var posicion = PreciosDeVenta.PosicionDeLista(nivelCliente);
        return posicion >= 0 && posicion < Precios.Count && Precios[posicion] > 0 ? Precios[posicion] : null;
    }

    /// <summary>Cuántas listas (de las 10) tienen precio para este ítem.</summary>
    public int ListasConPrecio => Precios.Count(p => p > 0);
}

/// <summary>Un cliente tal como lo necesita el vendedor: nivel de precio, crédito, saldo y vendedor asignado.</summary>
/// <param name="NivelPrecio"><c>Customers.PriceLevel</c> de Sage: <b>0-based</b> (0 = lista 1, 1 = lista 2…), verificado el 2026-10-02 contra facturas reales y contra el SDK.</param>
public sealed record ClienteVenta(
    string Id,
    string Nombre,
    string Contacto,
    string Telefono,
    string Email,
    int NivelPrecio,
    int DiasCredito,
    decimal LimiteCredito,
    decimal Saldo,
    string? Vendedor)
{
    /// <summary>Lista de precios que le toca, como la numera Sage en pantalla (1…10).</summary>
    public int ListaDePrecios => NivelPrecio + 1;

    public decimal CupoDisponible => LimiteCredito - Saldo;

    public bool SuperaCupo(decimal monto) => LimiteCredito > 0 && Saldo + monto > LimiteCredito;
}

/// <summary>Filtros de la consulta de inventario.</summary>
public sealed record FiltroItems(
    string? Texto = null,
    string? Categoria = null,
    bool IncluirEnsamblados = false,
    bool SoloConExistencia = false,
    int Maximo = 100);
