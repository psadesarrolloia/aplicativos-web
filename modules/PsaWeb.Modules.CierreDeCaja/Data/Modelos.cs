namespace PsaWeb.Modules.CierreDeCaja.Data;

/// <summary>Un renglón del detalle: total cobrado por tipo de recibo.</summary>
public sealed record CobroPorTipo(string Tipo, decimal Subtotal);

/// <summary>Resultado completo del cierre para un rango de fechas.</summary>
public sealed record ResultadoCierre(
    IReadOnlyList<CobroPorTipo> Cobros,
    decimal TotalCobros,
    decimal TotalVentas)
{
    /// <summary>Ventas menos cobros. Cero = cuadra.</summary>
    public decimal Diferencia => TotalVentas - TotalCobros;

    public bool SinMovimientos => Cobros.Count == 0 && TotalVentas == 0m;
}
