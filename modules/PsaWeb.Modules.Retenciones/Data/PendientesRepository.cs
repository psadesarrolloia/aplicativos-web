using Microsoft.EntityFrameworkCore;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.Retenciones.Data;

public sealed record EmpresaActiva(string Ruc, string Nombre, short Ambiente);

/// <summary>
/// Consultas de la base PeachEBills para el worker de retenciones: empresas
/// activas y facturas de compra pendientes de generar la retención.
/// Port de las consultas de <c>Program.cs</c>.
/// </summary>
public sealed class PendientesRepository
{
    private readonly IDbContextFactory<PeachEbillsContext> _contextFactory;

    public PendientesRepository(IDbContextFactory<PeachEbillsContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<IReadOnlyList<EmpresaActiva>> EmpresasActivasAsync(
        IEnumerable<string> omitirRucs, short? ambienteForzado, CancellationToken cancellationToken = default)
    {
        var omitir = omitirRucs.ToHashSet();

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Son ~18 empresas: se traen enteras y se cruzan en memoria (evita
        // traducir List.Contains, que en SQL Server viejo usa OPENJSON).
        var activos = await db.TransmitterStatus.AsNoTracking()
            .Where(s => s.IsActive)
            .Select(s => s.TransmitterRuc)
            .ToListAsync(cancellationToken);

        var nombres = await db.Transmitter.AsNoTracking()
            .Select(t => new { t.Ruc, Nombre = t.NameAlias ?? t.Name })
            .ToListAsync(cancellationToken);

        var ambientes = await db.CurrentAmbient.AsNoTracking()
            .Select(a => new { a.Ruc, a.AmbientDefault })
            .ToListAsync(cancellationToken);

        var mapaNombre = nombres.ToDictionary(x => x.Ruc, x => x.Nombre);
        var mapaAmbiente = ambientes.ToDictionary(x => x.Ruc, x => x.AmbientDefault);

        return activos
            .Where(ruc => !omitir.Contains(ruc))
            .Select(ruc => new EmpresaActiva(
                ruc,
                mapaNombre.GetValueOrDefault(ruc, ruc),
                ambienteForzado ?? (mapaAmbiente.TryGetValue(ruc, out var a) ? a : (short)2)))
            .ToList();
    }

    /// <summary>
    /// <c>PIPostOrder</c>s de <c>PurchaseOrderSync</c> que todavía no tienen una
    /// retención en <c>TaxWithHoldings</c> para ese ambiente y son posteriores al
    /// último procesado.
    /// </summary>
    public async Task<IReadOnlyList<string>> PendientesAsync(
        string ruc, short ambiente, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var maxPi = await db.Database
            .SqlQuery<string?>($@"
                SELECT TOP(1) PostOrderPeach AS Value
                FROM TaxWithHoldings
                WHERE TransmitterRuc = {ruc} AND Ambient = {ambiente}
                ORDER BY CAST(PostOrderPeach AS bigint) DESC")
            .FirstOrDefaultAsync(cancellationToken) ?? "0";

        var pendientes = await db.Database
            .SqlQuery<string>($@"
                SELECT PIPostOrder AS Value
                FROM PurchaseOrderSync
                WHERE RUCTransmitter = {ruc}
                  AND NOT (PIPostOrder IN (
                        SELECT PostOrderPeach FROM TaxWithHoldings
                        WHERE TransmitterRuc = {ruc} AND Ambient = {ambiente}))
                  AND CAST(PIPostOrder AS bigint) > CAST({maxPi} AS bigint)
                ORDER BY CAST(PIPostOrder AS bigint)")
            .ToListAsync(cancellationToken);

        return pendientes;
    }
}
