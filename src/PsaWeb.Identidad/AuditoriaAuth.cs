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

    // Accesos web (PLAN-ACCESOS-WEB): quién cambió qué a quién.
    public const string AccesoOtorgado = "acceso-otorgado";
    public const string AccesoQuitado = "acceso-quitado";
    public const string EmpresaActivada = "empresa-activada";
    public const string EmpresaDesactivada = "empresa-desactivada";
    public const string UsuarioSageCambiado = "usuario-sage";
    public const string PerfilCambiado = "perfil-cambiado";
    public const string InvitacionEnviada = "invitacion";
    public const string CuentaActivada = "cuenta-activada";
    public const string ClaveRecuperada = "clave-recuperada";
    public const string LoginIpNueva = "login-ip-nueva";
    public const string SegundoFactorActivado = "2fa-activado";
    public const string SegundoFactorDesactivado = "2fa-desactivado";
    public const string TokenExtensionRevocado = "token-revocado";
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
                Detalle = detalle is { Length: > 400 } ? detalle[..400] : detalle,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo registrar el evento de auditoría {Tipo} de {Usuario}", tipo, usuario);
        }
    }

    /// <summary>IP de la petición en curso (la real del cliente cuando hay proxy con ForwardedHeaders).</summary>
    public string? IpActual => _http.HttpContext?.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// true si el usuario ya ingresó antes con éxito y NUNCA desde esta IP (para el aviso por correo de ingreso nuevo).
    /// El primer ingreso de una cuenta no cuenta como "IP nueva".
    /// </summary>
    public async Task<bool> EsIpNuevaAsync(string usuario, string? ip, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(ip)) return false;
        try
        {
            await using var db = new PlataformaDbContext(_options);
            var exitos = db.EventosAuth.AsNoTracking()
                .Where(e => e.Usuario == usuario && (e.Tipo == TiposEventoAuth.LoginOk || e.Tipo == TiposEventoAuth.SegundoFactorOk));
            if (!await exitos.AnyAsync(cancellationToken)) return false;
            return !await exitos.AnyAsync(e => e.Ip == ip, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo evaluar si la IP es nueva para {Usuario}", usuario);
            return false;
        }
    }

    public async Task<IReadOnlyList<EventoAuth>> RecientesAsync(int top = 200, CancellationToken cancellationToken = default)
        => await BuscarAsync(null, null, top, cancellationToken);

    /// <summary>Eventos más recientes, filtrados por usuario (contiene, en el usuario o el detalle) y/o tipo exacto.</summary>
    public async Task<IReadOnlyList<EventoAuth>> BuscarAsync(
        string? usuario, string? tipo, int top = 200, CancellationToken cancellationToken = default)
    {
        await using var db = new PlataformaDbContext(_options);
        var q = db.EventosAuth.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(usuario))
        {
            var u = usuario.Trim();
            q = q.Where(e => e.Usuario.Contains(u) || (e.Detalle != null && e.Detalle.Contains(u)));
        }
        if (!string.IsNullOrWhiteSpace(tipo))
        {
            q = q.Where(e => e.Tipo == tipo);
        }
        return await q
            .OrderByDescending(e => e.Utc)
            .ThenByDescending(e => e.Id)
            .Take(top)
            .ToListAsync(cancellationToken);
    }
}
