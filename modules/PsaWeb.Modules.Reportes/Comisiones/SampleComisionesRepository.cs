using PsaWeb.Modules.Reportes.Pwc;

namespace PsaWeb.Modules.Reportes.Comisiones;

/// <summary>Datos ficticios para desarrollo sin tocar Sage 50 (con la misma forma que los reales).</summary>
internal sealed class SampleComisionesRepository : IComisionesRepository
{
    // Recibos 513 (antiguo), 5122 y 5130: con el rango 5122–5146 el filtro de TEXTO deja pasar el 513 (bug C2).
    private static readonly IReadOnlyList<CobroCrudo> Cobros = new List<CobroCrudo>
    {
        // Cliente 10 — una factura pagada por completo con retención (abono heredado = importe del recibo)
        new(9001, 10, "AGENCIA DEMO UNO S.A.", "001-001-000000201", new(2026, 6, 1), 1120m, 0m, 1120m,
            "5122", new(2026, 7, 13), -1026m, "GYE"),
        // Cliente 10 — factura con un cruce y otro cobro anterior: este recibo sólo aplicó 1.200 (abono heredado 1.480 ≠ importe del recibo)
        new(9002, 10, "AGENCIA DEMO UNO S.A.", "001-001-000000202", new(2026, 6, 5), 2300m, 0m, 2300m,
            "5130", new(2026, 7, 20), -1200m, "GYE"),
        // Cliente 20 — recibo antiguo (2013) que sólo entra por la comparación de TEXTO
        new(9003, 20, "CYEDE CIA. LTDA.", "001-001-003661", new(2012, 12, 20), 1075.20m, 0m, 1075.20m,
            "513", new(2013, 2, 13), -1065.60m, ""),
    };

    private static readonly IReadOnlyList<GrupoImpuestoCrudo> Grupos = new List<GrupoImpuestoCrudo>
    {
        new(9001, 0, "", -1000m), new(9001, 5, "IVA", -120m),
        new(9002, 0, "", -2000m), new(9002, 5, "IVA", -300m),
        new(9003, 0, "", -960m), new(9003, 5, "IVA", -115.20m),
    };

    private static readonly IReadOnlyDictionary<long, decimal> Retenciones = new Dictionary<long, decimal>
    {
        [9001] = 94m, [9002] = 270m, [9003] = 9.60m,
    };

    private static readonly IReadOnlyDictionary<long, decimal> Cruces = new Dictionary<long, decimal>
    {
        [9002] = -550m,
    };

    public Task<ResultadoComisiones> GenerarAsync(FiltroComisiones filtro, CancellationToken cancellationToken = default)
    {
        var elegidos = Cobros.Where(filtro.Coincide).ToList();
        return Task.FromResult(elegidos.Count == 0
            ? ResultadoComisiones.Vacio
            : ArmadorComisiones.Armar(elegidos, Grupos, Retenciones, Cruces, filtro.AbonoPorRecibo));
    }

    public Task<ResultadoComisiones> GenerarParaRucAsync(string ruc, FiltroComisiones filtro, CancellationToken cancellationToken = default)
        => GenerarAsync(filtro, cancellationToken);
}
