using Microsoft.EntityFrameworkCore;

namespace PsaWeb.Conciliacion.Data;

public sealed record ResultadoSubidaReporte(int Total, int Nuevos, int YaExistian, IReadOnlyList<FilaConError> Errores);

public interface IRepositorioComprobantesSri
{
    /// <exception cref="FormatoReporteInvalidoException">El reporte no tiene el formato esperado.</exception>
    Task<ResultadoSubidaReporte> GuardarReporteAsync(
        string ruc, string contenidoReporte, string subidoPor, CancellationToken cancellationToken = default);

    /// <summary>Guarda el resultado de una verificación de estado contra el WS del SRI (§13.4/§14).</summary>
    Task ActualizarEstadoAsync(
        long id, string estado, DateTime fechaVerificacionUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// Parsea el reporte y hace el upsert en <see cref="ComprobanteSriDescargado"/>.
/// Idempotente: una clave de acceso ya guardada nunca se pisa (los datos de un
/// comprobante autorizado no cambian una vez emitido) — ni entre subidas
/// distintas, ni si el archivo trae la misma clave repetida (el parser ya la
/// deduplica dentro del propio archivo).
/// </summary>
public sealed class RepositorioComprobantesSri(ConciliacionDbContext db) : IRepositorioComprobantesSri
{
    public async Task<ResultadoSubidaReporte> GuardarReporteAsync(
        string ruc, string contenidoReporte, string subidoPor, CancellationToken cancellationToken = default)
    {
        var resultado = LectorReporteComprobantesSri.Parsear(contenidoReporte, ruc);

        var clavesExistentes = (await db.ComprobantesSriDescargados
                .Where(c => c.Ruc == ruc)
                .Select(c => c.ClaveAcceso)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var nuevos = 0;
        var yaExistian = 0;
        foreach (var fila in resultado.Filas)
        {
            if (!clavesExistentes.Add(fila.ClaveAcceso))
            {
                yaExistian++;
                continue;
            }

            db.ComprobantesSriDescargados.Add(new ComprobanteSriDescargado
            {
                Ruc = ruc,
                ClaveAcceso = fila.ClaveAcceso,
                RucEmisor = fila.RucEmisor,
                RazonSocialEmisor = fila.RazonSocialEmisor,
                TipoComprobante = fila.TipoComprobante,
                SerieComprobante = fila.SerieComprobante,
                FechaAutorizacion = fila.FechaAutorizacion,
                FechaEmision = fila.FechaEmision,
                IdentificacionReceptor = fila.IdentificacionReceptor,
                Subtotal = fila.Subtotal,
                Iva = fila.Iva,
                Total = fila.Total,
                NumeroDocumentoModificado = fila.NumeroDocumentoModificado,
                SubidoPor = subidoPor,
            });
            nuevos++;
        }

        if (nuevos > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new ResultadoSubidaReporte(resultado.Filas.Count, nuevos, yaExistian, resultado.Errores);
    }

    public async Task ActualizarEstadoAsync(
        long id, string estado, DateTime fechaVerificacionUtc, CancellationToken cancellationToken = default)
    {
        var filas = await db.ComprobantesSriDescargados
            .Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Estado, estado)
                .SetProperty(c => c.FechaVerificacionEstado, fechaVerificacionUtc), cancellationToken);

        if (filas == 0)
        {
            throw new InvalidOperationException($"No se encontró el comprobante {id} para actualizar su estado.");
        }
    }
}
