using Microsoft.EntityFrameworkCore;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.ComprobantesElectronicos.Retenciones.Data;

public sealed record EmpresaActiva(string Ruc, string Nombre, short Ambiente);

/// <summary>
/// Consultas de la base PeachEBills para el worker de retenciones: empresas
/// activas y facturas de compra pendientes de generar la retención.
/// Port de las consultas de <c>Program.cs</c>. La lista base de empresas
/// activas vive en <c>PsaWeb.PeachEbills.Data.EmpresasActivasRepository</c>
/// (compartida con Conciliación SRI) — acá solo se le aplican los filtros
/// propios de Retenciones (<c>omitirRucs</c>/<c>ambienteForzado</c>).
/// </summary>
public sealed class PendientesRepository(
    IDbContextFactory<PeachEbillsContext> contextFactory, IEmpresasActivasRepository empresasActivas)
{
    private readonly IDbContextFactory<PeachEbillsContext> _contextFactory = contextFactory;

    public async Task<IReadOnlyList<EmpresaActiva>> EmpresasActivasAsync(
        IEnumerable<string> omitirRucs, short? ambienteForzado, CancellationToken cancellationToken = default)
    {
        var omitir = omitirRucs.ToHashSet();
        var todas = await empresasActivas.ObtenerAsync(cancellationToken);

        return todas
            .Where(e => !omitir.Contains(e.Ruc))
            .Select(e => new EmpresaActiva(e.Ruc, e.Nombre, ambienteForzado ?? e.AmbienteDefault))
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
