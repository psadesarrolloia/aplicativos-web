namespace PsaWeb.Modules.Reportes.Cheques;

/// <summary>Datos ficticios para desarrollo sin tocar Sage 50 (con la misma forma que los reales).</summary>
internal sealed class SampleChequesRepository : IChequesRepository
{
    private static PagoCheque Pago(long po, int dia, string beneficiario, string referencia, decimal monto)
        => new(po, new DateOnly(2026, 9, dia), beneficiario, ReferenciaPago.Analizar(referencia)!, monto);

    private static readonly IReadOnlyList<PagoConDetalle> Pagos = new List<PagoConDetalle>
    {
        new(Pago(5003, 18, "TONY VERA", "3094", 35m), new List<LineaPago>
        {
            new(0, "10302-311", "Banco Pacífico", "TONY VERA", -35m, "", ""),
            new(1, "20022-521", "Ctas por Pagar Gonzalo Rosero", "TONY VERA - VETERINARIO MIJO", 35m, "", ""),
        }),
        new(Pago(5002, 14, "VERONICA ROSERO", "6617", 2000m), new List<LineaPago>
        {
            new(0, "10302-311", "Banco Pacífico", "VERONICA ROSERO", -2000m, "", ""),
            new(1, "20022-521", "Ctas por Pagar Gonzalo Rosero", "SUELDO SEPTIEMBRE", 1500m, "FAC-0091", "VERONICA ROSERO"),
            new(2, "20022-522", "Ctas por Pagar Gonzalo Rosero", "ANTICIPO DE COMISIONES", 500m, "FAC-0092", "VERONICA ROSERO"),
        }),
        new(Pago(5001, 10, "PROVEEDOR CON UN NOMBRE MUY LARGO PARA PROBAR EL AJUSTE S.A.", "PI-4410", 1.5m), new List<LineaPago>
        {
            new(0, "10302-311", "Banco Pacífico", "PAGO", -1.5m, "", ""),
            new(1, "50101", "Gastos varios", "Servicio con descripción bastante larga para comprobar que se achica", 1.5m, "001-001-000123456", "PROVEEDOR"),
        }),
    };

    public Task<IReadOnlyList<PagoCheque>> ListarAsync(FiltroCheques filtro, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PagoCheque>>(
            Pagos.Select(p => p.Pago)
                .Where(p => p.Fecha >= filtro.Desde && p.Fecha <= filtro.Hasta)
                .Where(filtro.Coincide)
                .OrderByDescending(p => p.Fecha).ThenByDescending(p => p.PostOrder)
                .ToList());

    public Task<IReadOnlyList<PagoConDetalle>> DetallesAsync(IReadOnlyList<long> postOrders, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PagoConDetalle>>(
            postOrders.Distinct().Select(po => Pagos.FirstOrDefault(p => p.Pago.PostOrder == po)).Where(p => p is not null).Select(p => p!).ToList());

    public Task<IReadOnlyList<PagoConDetalle>> DetallesParaRucAsync(string ruc, IReadOnlyList<long> postOrders, CancellationToken cancellationToken = default)
        => DetallesAsync(postOrders, cancellationToken);
}
