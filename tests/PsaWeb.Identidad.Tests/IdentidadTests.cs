using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PsaWeb.Identidad;

namespace PsaWeb.Identidad.Tests;

/// <summary>
/// Verifica el store de Identity contra la base local <c>PsaWebPlataforma</c>:
/// política de claves, hashing, lockout y proveedor de TOTP.
/// </summary>
public class IdentidadTests : IDisposable
{
    private const string LocalConnectionString =
        @"Server=.\SQLEXPRESS;Database=PsaWebPlataforma;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15";

    private const string ClaveValida = "Psa.Web.2026!seg";

    private readonly ServiceProvider _sp;

    public IdentidadTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<PlataformaDbContext>(o => o.UseSqlServer(LocalConnectionString));
        services.AddIdentityCore<UsuarioApp>(o =>
        {
            o.Password.RequiredLength = 12;
            o.Password.RequireDigit = true;
            o.Password.RequireLowercase = true;
            o.Password.RequireUppercase = true;
            o.Password.RequireNonAlphanumeric = true;
            o.Password.RequiredUniqueChars = 4;
            o.Lockout.AllowedForNewUsers = true;
            o.Lockout.MaxFailedAccessAttempts = 5;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            o.User.RequireUniqueEmail = false;
            o.SignIn.RequireConfirmedAccount = false;
        })
        .AddEntityFrameworkStores<PlataformaDbContext>()
        .AddDefaultTokenProviders();

        _sp = services.BuildServiceProvider();
    }

    public void Dispose() => _sp.Dispose();

    private UserManager<UsuarioApp> Users() => _sp.GetRequiredService<UserManager<UsuarioApp>>();

    private static bool DbDisponible()
    {
        try
        {
            var o = new DbContextOptionsBuilder<PlataformaDbContext>().UseSqlServer(LocalConnectionString).Options;
            using var db = new PlataformaDbContext(o);
            return db.Database.CanConnect();
        }
        catch { return false; }
    }

    private async Task<UsuarioApp> CrearUsuario(UserManager<UsuarioApp> users, string? clave = null)
    {
        var nombre = "test_" + Guid.NewGuid().ToString("N")[..12];
        var u = new UsuarioApp { UserName = nombre, PeachUsername = nombre, Activo = true };
        var r = await users.CreateAsync(u, clave ?? ClaveValida);
        Assert.True(r.Succeeded, string.Join("; ", r.Errors.Select(e => e.Description)));
        return u;
    }

    [SkippableFact]
    public async Task Crea_usuario_con_clave_valida_y_guarda_hash_no_texto_plano()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        var users = Users();

        var u = await CrearUsuario(users);

        var fresco = await users.FindByIdAsync(u.Id);
        Assert.NotNull(fresco);
        Assert.False(string.IsNullOrEmpty(fresco!.PasswordHash));
        Assert.DoesNotContain(ClaveValida, fresco.PasswordHash!);
        Assert.True(await users.CheckPasswordAsync(fresco, ClaveValida));
        Assert.False(await users.CheckPasswordAsync(fresco, "otra-cosa"));
    }

    [SkippableFact]
    public async Task Rechaza_claves_que_no_cumplen_la_politica()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        var users = Users();

        var u = new UsuarioApp { UserName = "test_" + Guid.NewGuid().ToString("N")[..12] };
        var r = await users.CreateAsync(u, "corta1A!"); // < 12
        Assert.False(r.Succeeded);

        var r2 = await users.CreateAsync(u, "todominusculassinnada"); // sin mayúscula/dígito/símbolo
        Assert.False(r2.Succeeded);
    }

    [SkippableFact]
    public async Task Bloquea_la_cuenta_tras_cinco_fallos()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        var users = Users();
        var u = await CrearUsuario(users);

        for (var i = 0; i < 5; i++)
        {
            Assert.False(await users.CheckPasswordAsync(u, "clave-incorrecta"));
            await users.AccessFailedAsync(u);
        }

        Assert.True(await users.IsLockedOutAsync(u));
        var fin = await users.GetLockoutEndDateAsync(u);
        Assert.NotNull(fin);
        Assert.True(fin > DateTimeOffset.UtcNow);

        await users.ResetAccessFailedCountAsync(u);
        await users.SetLockoutEndDateAsync(u, null);
        Assert.False(await users.IsLockedOutAsync(u));
    }

    [SkippableFact]
    public async Task Provee_clave_de_autenticador_para_TOTP()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        var users = Users();
        var u = await CrearUsuario(users);

        await users.ResetAuthenticatorKeyAsync(u);
        var key = await users.GetAuthenticatorKeyAsync(u);
        Assert.False(string.IsNullOrWhiteSpace(key));
        Assert.True(key!.Length >= 16); // base32

        // Un código inventado no valida.
        var ok = await users.VerifyTwoFactorTokenAsync(u, TokenOptions.DefaultAuthenticatorProvider, "000000");
        Assert.False(ok);
    }
}
