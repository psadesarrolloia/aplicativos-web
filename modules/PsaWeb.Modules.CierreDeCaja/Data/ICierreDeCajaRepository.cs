namespace PsaWeb.Modules.CierreDeCaja.Data;

public interface ICierreDeCajaRepository
{
    /// <summary>
    /// Obtiene el detalle de cobros por tipo, el total cobrado y el total vendido
    /// para el rango <paramref name="desde"/>–<paramref name="hasta"/> (ambos inclusive),
    /// contra la empresa de sesión (o la de configuración si no hay shell).
    /// </summary>
    Task<ResultadoCierre> ObtenerAsync(DateOnly desde, DateOnly hasta, CancellationToken cancellationToken = default);

    /// <summary>
    /// Igual que <see cref="ObtenerAsync"/> pero contra una empresa explícita por RUC.
    /// Lo usa el endpoint de exportación, que no tiene el contexto del circuito Blazor.
    /// </summary>
    Task<ResultadoCierre> ObtenerParaRucAsync(
        string ruc, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken = default);
}
