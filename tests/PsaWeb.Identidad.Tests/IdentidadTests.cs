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

        services.AddScoped<GestorSegundoFactor>();

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

    [SkippableFact]
    public async Task GestorSegundoFactor_enrola_activa_y_desactiva()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        var users = Users();
        var gestor = _sp.GetRequiredService<GestorSegundoFactor>();
        var u = await CrearUsuario(users);

        Assert.False((await gestor.EstadoAsync(u)).Habilitado);

        var enrol = await gestor.PrepararEnrolamientoAsync(u);
        Assert.False(string.IsNullOrWhiteSpace(enrol.ClavePlana));
        Assert.Contains("otpauth://totp/", enrol.OtpAuthUri);
        Assert.Contains("<svg", enrol.QrSvg);

        // Código TOTP actual, calculado desde la clave base32 (RFC 6238).
        var codigo = TotpActual(enrol.ClavePlana);
        var (activado, recuperacion) = await gestor.ActivarAsync(u, codigo);
        Assert.True(activado);
        Assert.Equal(10, recuperacion.Count);

        var estado = await gestor.EstadoAsync(u);
        Assert.True(estado.Habilitado);
        Assert.Equal(10, estado.CodigosRecuperacionRestantes);

        // Código inválido no activa (probamos sobre un segundo usuario).
        var u2 = await CrearUsuario(users);
        await gestor.PrepararEnrolamientoAsync(u2);
        var (falla, _) = await gestor.ActivarAsync(u2, "000000");
        Assert.False(falla);

        await gestor.DesactivarAsync(u);
        Assert.False((await gestor.EstadoAsync(u)).Habilitado);
    }

    /// <summary>TOTP RFC 6238 (SHA1, 30 s, 6 dígitos) desde una clave base32.</summary>
    private static string TotpActual(string base32Key)
    {
        var key = Base32Decode(base32Key);
        var contador = (long)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var msg = new byte[8];
        for (var i = 7; i >= 0; i--) { msg[i] = (byte)(contador & 0xff); contador >>= 8; }

        using var hmac = new System.Security.Cryptography.HMACSHA1(key);
        var hash = hmac.ComputeHash(msg);
        var offset = hash[^1] & 0x0f;
        var bin = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16)
                  | (hash[offset + 2] << 8) | hash[offset + 3];
        return (bin % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string s)
    {
        const string abc = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        s = s.Trim().TrimEnd('=').ToUpperInvariant().Replace(" ", "");
        var bits = 0; var value = 0; var outp = new List<byte>();
        foreach (var c in s)
        {
            value = (value << 5) | abc.IndexOf(c);
            bits += 5;
            if (bits >= 8) { outp.Add((byte)((value >> (bits - 8)) & 0xff)); bits -= 8; }
        }
        return outp.ToArray();
    }
}
