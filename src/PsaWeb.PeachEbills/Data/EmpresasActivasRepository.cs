using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

/// <summary>Una empresa activa de <c>PeachEBills</c>, con su ambiente por defecto.</summary>
public sealed record EmpresaActiva(string Ruc, string Nombre, short AmbienteDefault);

/// <summary>
/// Lista las empresas activas (<c>TransmitterStatus.IsActive</c>) con nombre y
/// ambiente. Extraído de <c>PsaWeb.Modules.Retenciones.Data.PendientesRepository</c>
/// (§14.2 del plan de Conciliación SRI) al aparecer un segundo consumidor
/// cross-company (<c>VerificacionEstadoSriWorker</c>) — mismo criterio que ya
/// se usó para los lectores de compras (§13.1): compartir en vez de duplicar
/// o acoplar un módulo a otro.
/// </summary>
public interface IEmpresasActivasRepository
{
    Task<IReadOnlyList<EmpresaActiva>> ObtenerAsync(CancellationToken cancellationToken = default);
}

public sealed class EmpresasActivasRepository(IDbContextFactory<PeachEbillsContext> contextFactory) : IEmpresasActivasRepository
{
    public async Task<IReadOnlyList<EmpresaActiva>> ObtenerAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

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
            .Select(ruc => new EmpresaActiva(
                ruc,
                mapaNombre.GetValueOrDefault(ruc, ruc),
                mapaAmbiente.TryGetValue(ruc, out var a) ? a : (short)2))
            .ToList();
    }
}
