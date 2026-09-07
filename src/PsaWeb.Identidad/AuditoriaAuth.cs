using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PsaWeb.Identidad;

/// <summary>Tipos de evento de autenticación que se registran en <c>EventosAuth</c>.</summary>
public static class TiposEventoAuth
{
    public const string LoginOk = "login-ok";
    public const string LoginFallido = "login-fail";
    public const string LoginDeshabilitado = "login-deshab";
    public const string SegundoFactorPendiente = "2fa-pendiente";
    public const string SegundoFactorOk = "2fa-ok";
    public const string SegundoFactorFallido = "2fa-fail";
    public const string Bloqueo = "lockout";
    public const string Logout = "logout";
    public const string AdminAltaUsuario = "admin-alta";
    public const string AdminResetClave = "admin-reset";
    public const string AdminHabilitado = "admin-habilitado";
    public const string AdminDeshabilitado = "admin-deshabilitado";
    public const string Admin2faQuitado = "admin-2fa-quitado";
}

/// <summary>
/// Escribe y lee la auditoría de autenticación (<see cref="EventoAuth"/>). Nunca
/// hace fallar la operación que la origina: si la escritura falla, sólo loguea.
/// </summary>
public sealed class AuditoriaAuth
{
    private readonly DbContextOptions<PlataformaDbContext> _options;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<AuditoriaAuth> _logger;

    public AuditoriaAuth(
        DbContextOptions<PlataformaDbContext> options,
        IHttpContextAccessor http,
        ILogger<AuditoriaAuth> logger)
    {
        _options = options;
        _http = http;
        _logger = logger;
    }

    public async Task RegistrarAsync(string tipo, string? usuario, string? detalle = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = new PlataformaDbContext(_options);
            db.EventosAuth.Add(new EventoAuth
            {
                Utc = DateTime.UtcNow,
                Tipo = tipo,
                Usuario = (usuario ?? string.Empty).Length > 50 ? usuario![..50] : usuario ?? string.Empty,
                Ip = _http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
                Detalle = detalle,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo registrar el evento de auditoría {Tipo} de {Usuario}", tipo, usuario);
        }
    }

    public async Task<IReadOnlyList<EventoAuth>> RecientesAsync(int top = 200, CancellationToken cancellationToken = default)
    {
        await using var db = new PlataformaDbContext(_options);
        return await db.EventosAuth.AsNoTracking()
            .OrderByDescending(e => e.Utc)
            .ThenByDescending(e => e.Id)
            .Take(top)
            .ToListAsync(cancellationToken);
    }
}
