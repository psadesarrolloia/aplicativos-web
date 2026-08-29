namespace PsaWeb.Modules.CierreDeCaja.Data;

public interface ICierreDeCajaRepository
{
    /// <summary>
    /// Obtiene el detalle de cobros por tipo, el total cobrado y el total vendido
    /// para el rango <paramref name="desde"/>–<paramref name="hasta"/> (ambos inclusive).
    /// </summary>
    Task<ResultadoCierre> ObtenerAsync(DateOnly desde, DateOnly hasta, CancellationToken cancellationToken = default);
}
