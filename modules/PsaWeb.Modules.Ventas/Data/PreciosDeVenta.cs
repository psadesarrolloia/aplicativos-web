namespace PsaWeb.Modules.Ventas.Data;

/// <summary>
/// Reglas puras de los niveles de precio de Sage 50 (verificadas el 2026-10-02, SANCEV):
/// <c>Customers.PriceLevel</c> es 0-based y las listas del ítem (<c>LineItem.PriceLevel1Amount…10</c>, <c>PriceLevel</c> del SDK con
/// <c>Level</c> 1…10) son 1-based, así que la lista de un cliente es <c>PriceLevel + 1</c>.
/// </summary>
public static class PreciosDeVenta
{
    public const int NivelesMaximos = 10;

    /// <summary>Posición (0-based) del arreglo de precios que corresponde al nivel del cliente, o −1 si no es válido.</summary>
    public static int PosicionDeLista(int nivelCliente) =>
        nivelCliente is >= 0 and < NivelesMaximos ? nivelCliente : -1;

    /// <summary>Existencia = suma de las cantidades de compra (<c>MajorType 1</c>) y venta (<c>MajorType 2</c>, negativas) de <c>InventoryCosts</c>.</summary>
    public static decimal Existencia(IEnumerable<(int MajorType, decimal Cantidad)> movimientos) =>
        movimientos.Where(m => m.MajorType is 1 or 2).Sum(m => m.Cantidad);
}
