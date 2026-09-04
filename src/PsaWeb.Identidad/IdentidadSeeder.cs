using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PsaWeb.Identidad;

/// <summary>
/// Alta / actualización de usuarios de la plataforma. Se usa desde la
/// administración (F-Shell-4) y para sembrar el primer usuario en desarrollo.
/// </summary>
public sealed class IdentidadSeeder
{
    private readonly UserManager<UsuarioApp> _users;
    private readonly PlataformaDbContext _db;
    private readonly ILogger<IdentidadSeeder> _logger;

    public IdentidadSeeder(UserManager<UsuarioApp> users, PlataformaDbContext db, ILogger<IdentidadSeeder> logger)
    {
        _users = users;
        _db = db;
        _logger = logger;
    }

    /// <summary>Aplica migraciones pendientes de <c>PsaWebPlataforma</c>.</summary>
    public Task MigrarAsync(CancellationToken cancellationToken = default)
        => _db.Database.MigrateAsync(cancellationToken);

    /// <summary>
    /// Crea el usuario si no existe (no cambia la clave de uno existente).
    /// Devuelve true si lo creó.
    /// </summary>
    public async Task<bool> CrearSiNoExisteAsync(
        string usuario, string clave, string? email = null, string? nombreCompleto = null,
        string? peachUsername = null)
    {
        var existente = await _users.FindByNameAsync(usuario);
        if (existente is not null) return false;

        var nuevo = new UsuarioApp
        {
            UserName = usuario,
            Email = email,
            NombreCompleto = nombreCompleto,
            PeachUsername = string.IsNullOrWhiteSpace(peachUsername) ? usuario : peachUsername,
            Activo = true,
            EmailConfirmed = email is not null,
        };

        var r = await _users.CreateAsync(nuevo, clave);
        if (!r.Succeeded)
        {
            throw new InvalidOperationException(
                "No se pudo crear el usuario: " + string.Join("; ", r.Errors.Select(e => e.Description)));
        }

        _logger.LogInformation("Usuario de plataforma creado: {Usuario}", usuario);
        return true;
    }
}
