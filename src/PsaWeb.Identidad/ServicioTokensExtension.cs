using Microsoft.EntityFrameworkCore;

namespace PsaWeb.Identidad;

public sealed record TokenExtensionInfo(DateTime CreadoUtc, DateTime? UltimoUsoUtc);

/// <summary>
/// Alta, validación y revocación del token de API de la extensión de Chrome
/// (módulo de Conciliación SRI). Un solo token activo por usuario.
/// </summary>
public interface IServicioTokensExtension
{
    /// <summary>Genera un token nuevo para el usuario, revocando el anterior si tenía uno. Devuelve el token completo — se muestra una sola vez.</summary>
    Task<string> GenerarAsync(string usuarioId, CancellationToken cancellationToken = default);

    /// <summary>Valida un token recibido (header <c>Authorization: Bearer ...</c>). Devuelve el <c>UsuarioId</c> si es válido, o null.</summary>
    Task<string?> ValidarAsync(string? tokenCompleto, CancellationToken cancellationToken = default);

    /// <summary>Info del token activo del usuario (para la pantalla /mi-cuenta/extension), sin exponer el secreto.</summary>
    Task<TokenExtensionInfo?> ObtenerInfoAsync(string usuarioId, CancellationToken cancellationToken = default);

    Task RevocarAsync(string usuarioId, CancellationToken cancellationToken = default);
}

public sealed class ServicioTokensExtension(PlataformaDbContext db) : IServicioTokensExtension
{
    public async Task<string> GenerarAsync(string usuarioId, CancellationToken cancellationToken = default)
    {
        await RevocarAsync(usuarioId, cancellationToken);

        var (tokenCompleto, prefijo, hashSecreto) = GeneradorTokenExtension.Generar();
        db.TokensExtension.Add(new TokenExtension
        {
            UsuarioId = usuarioId,
            Prefijo = prefijo,
            HashSecreto = hashSecreto,
        });
        await db.SaveChangesAsync(cancellationToken);
        return tokenCompleto;
    }

    public async Task<string?> ValidarAsync(string? tokenCompleto, CancellationToken cancellationToken = default)
    {
        if (!GeneradorTokenExtension.TryDescomponer(tokenCompleto, out var prefijo, out var secreto))
        {
            return null;
        }

        var token = await db.TokensExtension
            .Where(t => t.Prefijo == prefijo && t.RevocadoUtc == null)
            .FirstOrDefaultAsync(cancellationToken);
        if (token is null)
        {
            return null;
        }

        if (!GeneradorTokenExtension.HashesIguales(GeneradorTokenExtension.Hash(secreto), token.HashSecreto))
        {
            return null;
        }

        token.UltimoUsoUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return token.UsuarioId;
    }

    public async Task<TokenExtensionInfo?> ObtenerInfoAsync(string usuarioId, CancellationToken cancellationToken = default)
    {
        var token = await db.TokensExtension
            .Where(t => t.UsuarioId == usuarioId && t.RevocadoUtc == null)
            .OrderByDescending(t => t.CreadoUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return token is null ? null : new TokenExtensionInfo(token.CreadoUtc, token.UltimoUsoUtc);
    }

    public async Task RevocarAsync(string usuarioId, CancellationToken cancellationToken = default)
    {
        var activos = await db.TokensExtension
            .Where(t => t.UsuarioId == usuarioId && t.RevocadoUtc == null)
            .ToListAsync(cancellationToken);
        if (activos.Count == 0)
        {
            return;
        }

        var ahora = DateTime.UtcNow;
        foreach (var token in activos)
        {
            token.RevocadoUtc = ahora;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
