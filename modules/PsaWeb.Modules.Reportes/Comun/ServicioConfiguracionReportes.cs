using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace PsaWeb.Modules.Reportes.Comun;

/// <summary>Implementación persistente (PsaWebPlataforma).</summary>
public sealed class ServicioConfiguracionEf(ReportesDbContext db) : IServicioConfiguracionReportes
{
    public async Task<T> ObtenerAsync<T>(string ruc, string reporte, CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var json = await db.ConfiguracionesReporte.AsNoTracking()
            .Where(c => c.Ruc == ruc && c.Reporte == reporte)
            .Select(c => c.Json)
            .FirstOrDefaultAsync(cancellationToken);
        return JsonConfig.Leer<T>(json);
    }

    public async Task GuardarAsync<T>(string ruc, string reporte, T valor, string? usuario, CancellationToken cancellationToken = default)
        where T : class
    {
        var fila = await ObtenerOCrearAsync(ruc, reporte, cancellationToken);
        fila.Json = JsonConfig.Escribir(valor);
        fila.ActualizadoPor = usuario;
        fila.ActualizadoEn = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<LogoEmpresa?> LogoAsync(string ruc, CancellationToken cancellationToken = default)
    {
        var fila = await db.ConfiguracionesReporte.AsNoTracking()
            .Where(c => c.Ruc == ruc && c.Reporte == ClavesReporte.Empresa)
            .Select(c => new { c.Logo, c.LogoTipo })
            .FirstOrDefaultAsync(cancellationToken);
        return fila?.Logo is { Length: > 0 } bytes
            ? new LogoEmpresa(bytes, fila.LogoTipo ?? "image/png")
            : null;
    }

    public async Task GuardarLogoAsync(string ruc, LogoEmpresa? logo, string? usuario, CancellationToken cancellationToken = default)
    {
        var fila = await ObtenerOCrearAsync(ruc, ClavesReporte.Empresa, cancellationToken);
        fila.Logo = logo?.Contenido;
        fila.LogoTipo = logo?.TipoMime;
        fila.ActualizadoPor = usuario;
        fila.ActualizadoEn = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ConfiguracionReporteEntidad> ObtenerOCrearAsync(string ruc, string reporte, CancellationToken ct)
    {
        var fila = await db.ConfiguracionesReporte.FirstOrDefaultAsync(c => c.Ruc == ruc && c.Reporte == reporte, ct);
        if (fila is null)
        {
            fila = new ConfiguracionReporteEntidad { Ruc = ruc, Reporte = reporte, Json = "{}" };
            db.ConfiguracionesReporte.Add(fila);
        }
        return fila;
    }
}

/// <summary>Implementación en memoria (dev sin <c>Plataforma:ConnectionString</c> y tests).</summary>
public sealed class ServicioConfiguracionMemoria : IServicioConfiguracionReportes
{
    private readonly ConcurrentDictionary<(string, string), string> _json = new();
    private readonly ConcurrentDictionary<string, LogoEmpresa> _logos = new();

    public Task<T> ObtenerAsync<T>(string ruc, string reporte, CancellationToken cancellationToken = default)
        where T : class, new()
        => Task.FromResult(JsonConfig.Leer<T>(_json.TryGetValue((ruc, reporte), out var j) ? j : null));

    public Task GuardarAsync<T>(string ruc, string reporte, T valor, string? usuario, CancellationToken cancellationToken = default)
        where T : class
    {
        _json[(ruc, reporte)] = JsonConfig.Escribir(valor);
        return Task.CompletedTask;
    }

    public Task<LogoEmpresa?> LogoAsync(string ruc, CancellationToken cancellationToken = default)
        => Task.FromResult(_logos.TryGetValue(ruc, out var l) ? l : null);

    public Task GuardarLogoAsync(string ruc, LogoEmpresa? logo, string? usuario, CancellationToken cancellationToken = default)
    {
        if (logo is null) _logos.TryRemove(ruc, out _);
        else _logos[ruc] = logo;
        return Task.CompletedTask;
    }
}
