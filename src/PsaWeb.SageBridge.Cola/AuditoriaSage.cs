using Microsoft.EntityFrameworkCore;
using PsaWeb.SageBridge.Cola.Data;

namespace PsaWeb.SageBridge.Cola;

/// <summary>Auditoría de los registros en Sage pedidos desde la web (tabla <c>AuditoriaRegistrosSage</c>).</summary>
public interface IAuditoriaSage
{
    Task RegistrarAsync(AuditoriaRegistroSage registro, CancellationToken cancellationToken = default);

    /// <summary>Historial de un documento (más reciente primero).</summary>
    Task<IReadOnlyList<AuditoriaRegistroSage>> DelDocumentoAsync(string ruc, string tercero, string documento,
        CancellationToken cancellationToken = default);
}

public sealed class AuditoriaSage(IDbContextFactory<SageBridgeDbContext> contextFactory, TimeProvider? reloj = null) : IAuditoriaSage
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    public async Task RegistrarAsync(AuditoriaRegistroSage registro, CancellationToken cancellationToken = default)
    {
        if (registro.FechaUtc == default) registro.FechaUtc = _reloj.GetUtcNow().UtcDateTime;
        if (registro.Detalle is { Length: > 2000 }) registro.Detalle = registro.Detalle[..2000];
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.Auditoria.Add(registro);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditoriaRegistroSage>> DelDocumentoAsync(string ruc, string tercero, string documento,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Auditoria.AsNoTracking()
            .Where(a => a.Ruc == ruc && a.Tercero == tercero && a.Documento == documento)
            .OrderByDescending(a => a.Id)
            .Take(50)
            .ToListAsync(cancellationToken);
    }
}
