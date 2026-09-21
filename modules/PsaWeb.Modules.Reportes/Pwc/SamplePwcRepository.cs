namespace PsaWeb.Modules.Reportes.Pwc;

/// <summary>Datos ficticios para desarrollo sin tocar Sage 50 (con la misma forma que los reales).</summary>
internal sealed class SamplePwcRepository : IPwcRepository
{
    private static readonly IReadOnlyList<CabeceraFacturaPwc> Cabeceras = new List<CabeceraFacturaPwc>
    {
        new(1001, "AGENCIA DEMO UNO S.A.", "001-001-000000101", new(2026, 8, 3), new(2026, 9, 2), 1120m, 94m,
            "CLIENTE FINAL A", "Av. Amazonas N12", "2026-0101-01", "GYE"),
        new(1002, "AGENCIA DEMO UNO S.A.", "001-001-000000102", new(2026, 8, 10), new(2026, 9, 9), 2240m, 728m,
            "CLIENTE FINAL B", "", "2026-0102", "UIO"),
        new(1003, "PUBLICIDAD DEMO DOS CIA. LTDA.", "001-001-000000103", new(2026, 8, 20), new(2026, 9, 19), 560m, 0m,
            "CLIENTE FINAL C", "", "SN", ""),
        new(1004, "PUBLICIDAD DEMO DOS CIA. LTDA.", "001-001-000000104", new(2026, 9, 1), new(2026, 10, 1), 1680m, 206.25m,
            "CLIENTE FINAL D", "", "PO-0007", "GYE"),
    };

    private static readonly IReadOnlyList<GrupoImpuestoCrudo> Grupos = new List<GrupoImpuestoCrudo>
    {
        new(1001, 0, "", -1000m), new(1001, 1, "", 0m), new(1001, 5, "IVA", -120m),
        new(1002, 0, "", -2000m), new(1002, 5, "IVA", -240m),
        new(1003, 0, "", -500m), new(1003, 5, "IVA", -60m),
        new(1004, 0, "", -1500m), new(1004, 5, "IVA", -180m),
    };

    private static readonly IReadOnlyList<RetencionCruda> Retenciones = new List<RetencionCruda>
    {
        new(1001, "IRF", 10m, "1% RET IMP RENTA"),
        new(1001, "IVA", 84m, "IVA 70%"),
        new(1002, "IRF", 60m, "309 - RF 3%"),
        new(1002, "IVA", 168m, "70% RET IVA"),
        new(1004, "IRF", 26.25m, "1.75% RET IMP RENTA"),
        new(1004, "IVA", 180m, "RETENCION IVA"),
    };

    public Task<OpcionesPwc> OpcionesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ArmadorPwc.Opciones(Cabeceras));

    public Task<ResultadoPwc> GenerarAsync(FiltroPwc filtro, CancellationToken cancellationToken = default)
    {
        var elegidas = Cabeceras.Where(filtro.Coincide).ToList();
        return Task.FromResult(elegidas.Count == 0
            ? ResultadoPwc.Vacio
            : new ResultadoPwc(ArmadorPwc.Armar(elegidas, Grupos, Retenciones)));
    }

    public Task<ResultadoPwc> GenerarParaRucAsync(string ruc, FiltroPwc filtro, CancellationToken cancellationToken = default)
        => GenerarAsync(filtro, cancellationToken);
}
