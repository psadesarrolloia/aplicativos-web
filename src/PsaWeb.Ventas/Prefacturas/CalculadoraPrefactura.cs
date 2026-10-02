using System.Globalization;

namespace PsaWeb.Ventas.Prefacturas;

/// <summary>
/// Cálculo de montos igual al de Sage 50 (verificado el 2026-10-02 con una factura creada por el SDK en SANCEV: 2 × 6,95 = 13,90 y 3 × 46,51 = 139,53;
/// IVA 15 % de 153,43 = 23,01; total 176,44): cada línea se redondea a 2 decimales y el impuesto se calcula una sola vez sobre el subtotal.
/// </summary>
public static class CalculadoraPrefactura
{
    public static decimal Redondear(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    public static decimal MontoDeLinea(decimal cantidad, decimal precioUnitario) => Redondear(cantidad * precioUnitario);

    public static IReadOnlyList<LineaPrefactura> Calcular(IReadOnlyList<LineaSolicitada> lineas) =>
        lineas.Select((l, i) => new LineaPrefactura(
            i + 1, l.ItemId.Trim(), l.Descripcion.Trim(), l.UnidadMedida.Trim(), l.Cantidad, l.PrecioLista, l.PrecioUnitario,
            l.PrecioManual, MontoDeLinea(l.Cantidad, l.PrecioUnitario), l.ExistenciaAlEmitir)).ToList();

    public static (decimal Subtotal, decimal Iva, decimal Total) Totales(IEnumerable<LineaPrefactura> lineas, bool aplicaIva, decimal porcentajeIva)
    {
        var subtotal = lineas.Sum(l => l.Monto);
        var iva = aplicaIva ? Redondear(subtotal * porcentajeIva / 100m) : 0m;
        return (subtotal, iva, subtotal + iva);
    }

    /// <summary>Descripción de términos tal como la escribe Sage («Net 1 Day», «Net 30 Days»); vacío si el cliente no tiene días de crédito.</summary>
    public static string TerminosSage(int diasCredito) => diasCredito switch
    {
        <= 0 => string.Empty,
        1 => "Net 1 Day",
        _ => "Net " + diasCredito.ToString(CultureInfo.InvariantCulture) + " Days",
    };
}

/// <summary>Arma la etiqueta que Contabilidad imprime en el campo «orden de compra del cliente» de la factura (p. ej. <c>PONCE DIEGO (EQU)</c>).</summary>
public static class EtiquetasVendedor
{
    public const string Equipos = "EQU";
    public const string Tableros = "TAB";

    /// <summary>
    /// De «DIEGO PONCE» a «PONCE DIEGO (EQU)»: con dos palabras invierte el orden (nombre apellido → apellido nombre) y con otra cantidad deja el
    /// texto como está, porque no hay forma segura de adivinar los apellidos. Se quita el sufijo « TABLEROS» y las siglas de los reps de tableros no se tocan.
    /// </summary>
    public static string Sugerir(string vendedorSage, string tipo)
    {
        var nombre = (vendedorSage ?? string.Empty).Trim().ToUpperInvariant();
        if (nombre.EndsWith(" TABLEROS", StringComparison.Ordinal)) nombre = nombre[..^" TABLEROS".Length].Trim();
        var partes = nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length == 2) nombre = partes[1] + " " + partes[0];
        return nombre.Length == 0 ? string.Empty : nombre + " (" + tipo + ")";
    }
}
