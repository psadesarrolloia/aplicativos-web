namespace PsaWeb.Modules.CierreDeCaja.Data;

/// <summary>
/// Datos ficticios para desarrollo cuando no hay cadena de conexión a Sage 50
/// (p. ej. en PREDATOR contra el DSN de ejemplo, que no tiene las tablas reales).
/// Deja la pantalla 100% funcional sin tocar producción.
/// </summary>
internal sealed class SampleCierreDeCajaRepository : ICierreDeCajaRepository
{
    public Task<ResultadoCierre> ObtenerAsync(DateOnly desde, DateOnly hasta, CancellationToken cancellationToken = default)
    {
        // Rango sin días => sin movimientos (para poder ver ese estado en la UI).
        if (desde > hasta)
        {
            return Task.FromResult(new ResultadoCierre(Array.Empty<CobroPorTipo>(), 0m, 0m));
        }

        var cobros = new List<CobroPorTipo>
        {
            new("Efectivo", 4_820.50m),
            new("Transferencia", 6_180.00m),
            new("Cheque", 1_480.00m),
        };

        var totalCobros = cobros.Sum(c => c.Subtotal); // 12 480,50
        var totalVentas = 12_480.50m;                  // cuadra: diferencia 0

        return Task.FromResult(new ResultadoCierre(cobros, totalCobros, totalVentas));
    }
}
