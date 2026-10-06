using Microsoft.EntityFrameworkCore;
using PsaWeb.SageBridge.Cola.Data;
using PsaWeb.SageBridge.Contratos;

namespace PsaWeb.SageBridge.Cola;

public sealed record ResultadoEncolar(TrabajoSage Trabajo, bool Nuevo);

public sealed record FiltroTrabajos(string? Ruc = null, string? Estado = null, string? Tipo = null, int Maximo = 100);

public sealed record ResumenCola(string Ruc, string Estado, int Cantidad);

/// <summary>
/// Lado web de la cola del Sage Bridge: encolar (idempotente), consultar, cancelar y reintentar trabajos, y
/// administrar la configuración por empresa. La web nunca toca el SDK de Sage: solo esta cola.
/// </summary>
public interface IColaSage
{
    Task<ResultadoEncolar> EncolarAsync(string ruc, string tipo, string? payloadJson, string claveIdempotencia,
        string usuario, CancellationToken cancellationToken = default);

    Task<TrabajoSage?> ObtenerAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrabajoSage>> ListarAsync(FiltroTrabajos filtro, CancellationToken cancellationToken = default);

    /// <summary>Cantidad de trabajos por empresa y estado (sin los terminados hace más de 7 días).</summary>
    Task<IReadOnlyList<ResumenCola>> ResumenAsync(CancellationToken cancellationToken = default);

    /// <summary>Cancela un trabajo que todavía no tomó el Bridge. Devuelve falso si ya no estaba en cola.</summary>
    Task<bool> CancelarAsync(long id, string usuario, CancellationToken cancellationToken = default);

    /// <summary>Vuelve a poner en cola un trabajo en Error o Cancelado (reinicia los intentos).</summary>
    Task<bool> ReintentarAsync(long id, string usuario, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LatidoBridge>> LatidosAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmpresaBridge>> EmpresasAsync(CancellationToken cancellationToken = default);

    /// <summary>Crea o actualiza la configuración de una empresa. Lanza <see cref="ArgumentException"/> si la ventana es inválida.</summary>
    Task<EmpresaBridge> GuardarEmpresaAsync(string ruc, bool habilitada, string? ventana, string? nota, string usuario,
        CancellationToken cancellationToken = default);
}

public sealed class ColaSage(IDbContextFactory<SageBridgeDbContext> contextFactory, TimeProvider? reloj = null) : IColaSage
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    public async Task<ResultadoEncolar> EncolarAsync(string ruc, string tipo, string? payloadJson, string claveIdempotencia,
        string usuario, CancellationToken cancellationToken = default)
    {
        Validar(ruc, tipo, claveIdempotencia);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existente = await db.Trabajos.AsNoTracking().FirstOrDefaultAsync(
            t => t.Ruc == ruc && t.Tipo == tipo && t.ClaveIdempotencia == claveIdempotencia, cancellationToken);
        if (existente is not null)
        {
            return new ResultadoEncolar(existente, Nuevo: false);
        }

        var trabajo = new TrabajoSage
        {
            Ruc = ruc,
            Tipo = tipo,
            Estado = EstadosTrabajo.EnCola,
            ClaveIdempotencia = claveIdempotencia,
            PayloadJson = payloadJson,
            CreadoPor = usuario,
            CreadoUtc = _reloj.GetUtcNow().UtcDateTime,
        };
        db.Trabajos.Add(trabajo);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new ResultadoEncolar(trabajo, Nuevo: true);
        }
        catch (DbUpdateException)
        {
            // Dos pedidos simultáneos con la misma clave: gana el primero, el segundo recibe ese trabajo.
            await using var db2 = await contextFactory.CreateDbContextAsync(cancellationToken);
            var ganador = await db2.Trabajos.AsNoTracking().FirstOrDefaultAsync(
                t => t.Ruc == ruc && t.Tipo == tipo && t.ClaveIdempotencia == claveIdempotencia, cancellationToken);
            if (ganador is null)
            {
                throw;
            }

            return new ResultadoEncolar(ganador, Nuevo: false);
        }
    }

    public async Task<TrabajoSage?> ObtenerAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Trabajos.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<TrabajoSage>> ListarAsync(FiltroTrabajos filtro, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var q = db.Trabajos.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(filtro.Ruc)) q = q.Where(t => t.Ruc == filtro.Ruc);
        if (!string.IsNullOrWhiteSpace(filtro.Estado)) q = q.Where(t => t.Estado == filtro.Estado);
        if (!string.IsNullOrWhiteSpace(filtro.Tipo)) q = q.Where(t => t.Tipo == filtro.Tipo);
        return await q.OrderByDescending(t => t.Id).Take(Math.Clamp(filtro.Maximo, 1, 1000)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ResumenCola>> ResumenAsync(CancellationToken cancellationToken = default)
    {
        var desde = _reloj.GetUtcNow().UtcDateTime.AddDays(-7);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var filas = await db.Trabajos.AsNoTracking()
            .Where(t => t.TerminadoUtc == null || t.TerminadoUtc >= desde)
            .GroupBy(t => new { t.Ruc, t.Estado })
            .Select(g => new { g.Key.Ruc, g.Key.Estado, Cantidad = g.Count() })
            .ToListAsync(cancellationToken);
        return filas.Select(f => new ResumenCola(f.Ruc, f.Estado, f.Cantidad))
            .OrderBy(f => f.Ruc).ThenBy(f => f.Estado).ToList();
    }

    public async Task<bool> CancelarAsync(long id, string usuario, CancellationToken cancellationToken = default)
    {
        var ahora = _reloj.GetUtcNow().UtcDateTime;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var filas = await db.Trabajos
            .Where(t => t.Id == id && t.Estado == EstadosTrabajo.EnCola)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Estado, EstadosTrabajo.Cancelado)
                .SetProperty(t => t.Error, $"Cancelado por {usuario}.")
                .SetProperty(t => t.TerminadoUtc, ahora), cancellationToken);
        return filas == 1;
    }

    public async Task<bool> ReintentarAsync(long id, string usuario, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var filas = await db.Trabajos
            .Where(t => t.Id == id && (t.Estado == EstadosTrabajo.Error || t.Estado == EstadosTrabajo.Cancelado))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Estado, EstadosTrabajo.EnCola)
                .SetProperty(t => t.Intentos, 0)
                .SetProperty(t => t.NoAntesDeUtc, (DateTime?)null)
                .SetProperty(t => t.TomadoPor, (string?)null)
                .SetProperty(t => t.TomadoHastaUtc, (DateTime?)null)
                .SetProperty(t => t.TerminadoUtc, (DateTime?)null)
                .SetProperty(t => t.Error, (string?)null), cancellationToken);
        return filas == 1;
    }

    public async Task<IReadOnlyList<LatidoBridge>> LatidosAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Latidos.AsNoTracking().OrderByDescending(l => l.UltimoLatidoUtc).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EmpresaBridge>> EmpresasAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Empresas.AsNoTracking().OrderBy(e => e.Ruc).ToListAsync(cancellationToken);
    }

    public async Task<EmpresaBridge> GuardarEmpresaAsync(string ruc, bool habilitada, string? ventana, string? nota, string usuario,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ruc))
        {
            throw new ArgumentException("Falta el RUC.", nameof(ruc));
        }

        var ventanaNormalizada = string.IsNullOrWhiteSpace(ventana) ? null : ventana.Trim();
        if (!VentanaMantenimiento.TryParsear(ventanaNormalizada, out var v))
        {
            throw new ArgumentException($"Ventana inválida «{ventana}». Usa HH:mm-HH:mm (p. ej. 22:00-06:00).", nameof(ventana));
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var empresa = await db.Empresas.FirstOrDefaultAsync(e => e.Ruc == ruc, cancellationToken);
        if (empresa is null)
        {
            empresa = new EmpresaBridge { Ruc = ruc };
            db.Empresas.Add(empresa);
        }

        empresa.Habilitada = habilitada;
        empresa.Ventana = ventanaNormalizada is null ? null : v!.ToString();
        empresa.Nota = string.IsNullOrWhiteSpace(nota) ? null : nota.Trim();
        empresa.ModificadoPor = usuario;
        empresa.ModificadoUtc = _reloj.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return empresa;
    }

    private static void Validar(string ruc, string tipo, string claveIdempotencia)
    {
        if (string.IsNullOrWhiteSpace(ruc) || ruc.Length > 13)
        {
            throw new ArgumentException("RUC inválido.", nameof(ruc));
        }

        if (!TiposTrabajo.Todos.Contains(tipo))
        {
            throw new ArgumentException($"Tipo de trabajo desconocido: {tipo}.", nameof(tipo));
        }

        if (string.IsNullOrWhiteSpace(claveIdempotencia) || claveIdempotencia.Length > 200)
        {
            throw new ArgumentException("La clave de idempotencia es obligatoria (máx. 200 caracteres).", nameof(claveIdempotencia));
        }
    }
}
