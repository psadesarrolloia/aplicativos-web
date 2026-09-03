namespace PsaWeb.Comprobantes.Sri;

/// <summary>
/// Constantes de los diarios de Sage 50 usadas en las consultas ODBC
/// (port de <c>PeachJournalMetadata</c>).
/// </summary>
public static class DiarioSage
{
    /// <summary><c>JrnlKey_Journal</c> del diario de ventas.</summary>
    public const int Ventas = 3;

    /// <summary><c>JrnlKey_Journal</c> del diario de compras.</summary>
    public const int Compras = 4;

    /// <summary><c>JrnlKey_Journal</c> de la orden de compra (para leer <c>GoodThruDate</c>).</summary>
    public const int OrdenCompra = 10;

    public const int JournalExFacturaVenta = 8;
    public const int JournalExNotaCreditoVenta = 9;
    public const int JournalExFacturaCompra = 11;
    public const int JournalExNotaCreditoCompra = 12;
}
