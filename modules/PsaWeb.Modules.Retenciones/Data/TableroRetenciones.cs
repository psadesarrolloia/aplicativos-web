using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.Retenciones.Data;

/// <summary>Una fila del tablero: empresa activa + cuántas retenciones tiene pendientes.</summary>
public sealed record FilaTablero(
    string Ruc,
    string Nombre,
    short Ambiente,
    int Pendientes,
    DateTime? UltimaEmision,
    string? UltimoNumero,
    string? Error);

/// <summary>Una retención ya guardada, para el historial reciente.</summary>
public sealed record RetencionReciente(
    string Ruc,
    string Empresa,
    string Numero,
    DateTime Fecha,
    string Secuencial,
    string? Contacto,
    string? DatilId,
    short Ambiente);

/// <summary>
/// Consultas de solo lectura para la página del módulo: panorama por empresa
/// (pendientes + última emisión) e historial reciente. No emite nada.
/// </summary>
public sealed class TableroRetenciones
{
    private readonly IDbContextFactory<PeachEbillsContext> _contextFactory;
    private readonly PendientesRepository _pendientes;
    private readonly RetencionesOptions _opciones;

    public TableroRetenciones(
        IDbContextFactory<PeachEbillsContext> contextFactory,
        PendientesRepository pendientes,
        IOptions<RetencionesOptions> opciones)
    {
        _contextFactory = contextFactory;
        _pendientes = pendientes;
        _opciones = opciones.Value;
    }

    public async Task<IReadOnlyList<FilaTablero>> PanoramaAsync(CancellationToken cancellationToken = default)
    {
        var empresas = await _pendientes.EmpresasActivasAsync(
            _opciones.OmitirRucs, _opciones.AmbienteForzado, cancellationToken);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var filas = new List<FilaTablero>(empresas.Count);
        foreach (var empresa in empresas)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int pendientes;
            string? error = null;
            try
            {
                pendientes = (await _pendientes.PendientesAsync(empresa.Ruc, empresa.Ambiente, cancellationToken)).Count;
            }
            catch (Exception ex)
            {
                pendientes = 0;
                error = ex.Message;
            }

            var ultima = await db.TaxWithHoldings.AsNoTracking()
                .Where(t => t.TransmitterRuc == empresa.Ruc && t.Ambient == empresa.Ambiente)
                .OrderByDescending(t => t.DateIssued)
                .ThenByDescending(t => t.Thid)
                .Select(t => new { t.DateIssued, t.NumberPech })
                .FirstOrDefaultAsync(cancellationToken);

            filas.Add(new FilaTablero(
                empresa.Ruc, empresa.Nombre, empresa.Ambiente, pendientes,
                ultima?.DateIssued, ultima?.NumberPech, error));
        }

        return filas;
    }

    /// <summary>
    /// Retenciones guardadas de todas las empresas, opcionalmente acotadas por
    /// rango de fechas de emisión (<paramref name="desde"/> inclusive,
    /// <paramref name="hasta"/> inclusive), las <paramref name="top"/> más nuevas.
    /// </summary>
    public Task<IReadOnlyList<RetencionReciente>> RecientesAsync(
        DateTime? desde = null, DateTime? hasta = null, int top = 200,
        CancellationToken cancellationToken = default)
        => RecientesInternoAsync(null, desde, hasta, top, cancellationToken);

    /// <summary>Igual que <see cref="RecientesAsync"/> pero de una sola empresa.</summary>
    public Task<IReadOnlyList<RetencionReciente>> RecientesPorEmpresaAsync(
        string ruc, DateTime? desde = null, DateTime? hasta = null, int top = 200,
        CancellationToken cancellationToken = default)
        => RecientesInternoAsync(ruc, desde, hasta, top, cancellationToken);

    private async Task<IReadOnlyList<RetencionReciente>> RecientesInternoAsync(
        string? rucFiltro, DateTime? desde, DateTime? hasta, int top, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.TaxWithHoldings.AsNoTracking();
        if (rucFiltro is not null)
        {
            query = query.Where(t => t.TransmitterRuc == rucFiltro);
        }
        if (desde is { } d)
        {
            query = query.Where(t => t.DateIssued >= d.Date);
        }
        if (hasta is { } h)
        {
            var finExclusivo = h.Date.AddDays(1);
            query = query.Where(t => t.DateIssued < finExclusivo);
        }

        var recientes = await query
            .OrderByDescending(t => t.DateIssued)
            .ThenByDescending(t => t.Thid)
            .Take(top)
            .Select(t => new
            {
                t.TransmitterRuc,
                t.NumberPech,
                t.DateIssued,
                t.Secuencial,
                t.Contact,
                t.DatilId,
                t.Ambient,
            })
            .ToListAsync(cancellationToken);

        var nombres = await db.Transmitter.AsNoTracking()
            .Select(x => new { x.Ruc, Nombre = x.NameAlias ?? x.Name })
            .ToListAsync(cancellationToken);
        var mapaNombre = nombres.ToDictionary(x => x.Ruc, x => x.Nombre);

        return recientes
            .Select(t => new RetencionReciente(
                t.TransmitterRuc,
                mapaNombre.GetValueOrDefault(t.TransmitterRuc, t.TransmitterRuc),
                t.NumberPech,
                t.DateIssued,
                t.Secuencial,
                t.Contact,
                t.DatilId,
                t.Ambient))
            .ToList();
    }
}
