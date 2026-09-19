using Microsoft.EntityFrameworkCore;

namespace PsaWeb.Conciliacion.Data;

/// <summary>
/// Un comprobante ya guardado, para consumo del motor de conciliación (Set A,
/// F3) — DTO neutral en vez de la entidad de EF, para no acoplar al motor
/// (lógica pura, sin EF) con el tracking de <see cref="ConciliacionDbContext"/>.
/// </summary>
public sealed record ComprobanteSriGuardado(
    long Id,
    string ClaveAcceso,
    string RucEmisor,
    string RazonSocialEmisor,
    string TipoComprobante,
    string SerieComprobante,
    DateTime FechaAutorizacion,
    DateOnly FechaEmision,
    decimal Subtotal,
    decimal Iva,
    decimal Total,
    string? NumeroDocumentoModificado,
    string? Estado,
    DateTime? FechaVerificacionEstado,
    bool DiferenciaAceptada = false,
    string? DiferenciaAceptadaPor = null,
    DateTime? DiferenciaAceptadaUtc = null,
    string? ComentarioAceptacion = null);

/// <summary>Lector de Set A (comprobantes del SRI ya en el staging) para una empresa y un período.</summary>
public interface ILectorComprobantesSri
{
    Task<IReadOnlyList<ComprobanteSriGuardado>> ObtenerAsync(
        string ruc, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken = default);
}

public sealed class LectorComprobantesSri(ConciliacionDbContext db) : ILectorComprobantesSri
{
    public async Task<IReadOnlyList<ComprobanteSriGuardado>> ObtenerAsync(
        string ruc, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken = default) =>
        await db.ComprobantesSriDescargados
            .Where(c => c.Ruc == ruc && c.FechaEmision >= desde && c.FechaEmision <= hasta)
            .OrderBy(c => c.FechaEmision)
            .Select(c => new ComprobanteSriGuardado(
                c.Id, c.ClaveAcceso, c.RucEmisor, c.RazonSocialEmisor, c.TipoComprobante, c.SerieComprobante,
                c.FechaAutorizacion, c.FechaEmision, c.Subtotal, c.Iva, c.Total, c.NumeroDocumentoModificado,
                c.Estado, c.FechaVerificacionEstado, c.DiferenciaAceptada, c.DiferenciaAceptadaPor,
                c.DiferenciaAceptadaUtc, c.ComentarioAceptacion))
            .ToListAsync(cancellationToken);
}
