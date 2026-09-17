using Microsoft.EntityFrameworkCore;
using PsaWeb.Identidad;

namespace PsaWeb.Identidad.Tests;

/// <summary>Pruebas puras del generador/validador de tokens — sin base de datos.</summary>
public class GeneradorTokenExtensionTests
{
    [Fact]
    public void Genera_un_token_con_el_formato_esperado()
    {
        var (tokenCompleto, prefijo, hashSecreto) = GeneradorTokenExtension.Generar();

        Assert.StartsWith("psaext_", tokenCompleto);
        Assert.Equal(8, prefijo.Length);
        Assert.Equal(64, hashSecreto.Length); // SHA-256 en hex
        Assert.Equal("psaext_".Length + 8 + 48, tokenCompleto.Length);
    }

    [Fact]
    public void Descompone_un_token_valido_y_el_hash_coincide()
    {
        var (tokenCompleto, prefijo, hashSecreto) = GeneradorTokenExtension.Generar();

        Assert.True(GeneradorTokenExtension.TryDescomponer(tokenCompleto, out var prefijoLeido, out var secreto));
        Assert.Equal(prefijo, prefijoLeido);
        Assert.True(GeneradorTokenExtension.HashesIguales(GeneradorTokenExtension.Hash(secreto), hashSecreto));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sin-el-prefijo-correcto")]
    [InlineData("psaext_muycorto")]
    public void Rechaza_tokens_con_formato_invalido(string? token)
    {
        Assert.False(GeneradorTokenExtension.TryDescomponer(token, out _, out _));
    }

    [Fact]
    public void Dos_tokens_generados_no_se_repiten()
    {
        var (token1, _, _) = GeneradorTokenExtension.Generar();
        var (token2, _, _) = GeneradorTokenExtension.Generar();
        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public void HashesIguales_es_false_ante_hash_con_formato_invalido()
    {
        Assert.False(GeneradorTokenExtension.HashesIguales("no-es-hex", "tampoco"));
    }
}

/// <summary>Pruebas del servicio contra la base local <c>PsaWebPlataforma</c> (se saltean si no está disponible).</summary>
public class ServicioTokensExtensionTests : IAsyncLifetime
{
    private const string LocalConnectionString =
        @"Server=.\SQLEXPRESS;Database=PsaWebPlataforma;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15";

    private readonly List<string> _usuariosDePrueba = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (!DbDisponible())
        {
            return;
        }

        await using var db = Db();
        var tokens = await db.TokensExtension.Where(t => _usuariosDePrueba.Contains(t.UsuarioId)).ToListAsync();
        db.TokensExtension.RemoveRange(tokens);
        await db.SaveChangesAsync();
    }

    private static bool DbDisponible()
    {
        try
        {
            using var db = Db();
            return db.Database.CanConnect();
        }
        catch { return false; }
    }

    private static PlataformaDbContext Db() =>
        new(new DbContextOptionsBuilder<PlataformaDbContext>().UseSqlServer(LocalConnectionString).Options);

    private string UsuarioDePrueba()
    {
        var id = "test_" + Guid.NewGuid().ToString("N")[..12];
        _usuariosDePrueba.Add(id);
        return id;
    }

    [SkippableFact]
    public async Task Genera_un_token_y_lo_valida()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using var db = Db();
        var servicio = new ServicioTokensExtension(db);
        var usuarioId = UsuarioDePrueba();

        var token = await servicio.GenerarAsync(usuarioId);
        var usuarioValidado = await servicio.ValidarAsync(token);

        Assert.Equal(usuarioId, usuarioValidado);
    }

    [SkippableFact]
    public async Task Un_token_invalido_o_con_secreto_incorrecto_no_valida()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using var db = Db();
        var servicio = new ServicioTokensExtension(db);
        var usuarioId = UsuarioDePrueba();

        var token = await servicio.GenerarAsync(usuarioId);
        var prefijo = token[..(7 + 8)]; // "psaext_" + 8 de prefijo
        var tokenConSecretoMalo = prefijo + new string('0', 48);

        Assert.Null(await servicio.ValidarAsync(tokenConSecretoMalo));
        Assert.Null(await servicio.ValidarAsync("psaext_" + new string('a', 56)));
        Assert.Null(await servicio.ValidarAsync(null));
    }

    [SkippableFact]
    public async Task Generar_de_nuevo_revoca_el_token_anterior()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using var db = Db();
        var servicio = new ServicioTokensExtension(db);
        var usuarioId = UsuarioDePrueba();

        var tokenViejo = await servicio.GenerarAsync(usuarioId);
        var tokenNuevo = await servicio.GenerarAsync(usuarioId);

        Assert.Null(await servicio.ValidarAsync(tokenViejo));
        Assert.Equal(usuarioId, await servicio.ValidarAsync(tokenNuevo));
    }

    [SkippableFact]
    public async Task Revocar_deja_sin_token_activo()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using var db = Db();
        var servicio = new ServicioTokensExtension(db);
        var usuarioId = UsuarioDePrueba();

        var token = await servicio.GenerarAsync(usuarioId);
        await servicio.RevocarAsync(usuarioId);

        Assert.Null(await servicio.ValidarAsync(token));
        Assert.Null(await servicio.ObtenerInfoAsync(usuarioId));
    }

    [SkippableFact]
    public async Task ObtenerInfo_no_expone_el_secreto_y_refleja_el_ultimo_uso()
    {
        Skip.IfNot(DbDisponible(), "PsaWebPlataforma local no disponible.");
        await using var db = Db();
        var servicio = new ServicioTokensExtension(db);
        var usuarioId = UsuarioDePrueba();

        var token = await servicio.GenerarAsync(usuarioId);
        var infoRecienCreado = await servicio.ObtenerInfoAsync(usuarioId);
        Assert.NotNull(infoRecienCreado);
        Assert.Null(infoRecienCreado!.UltimoUsoUtc); // recién creado, todavía no se usó para validar nada

        await servicio.ValidarAsync(token);
        var infoTrasUso = await servicio.ObtenerInfoAsync(usuarioId);
        Assert.NotNull(infoTrasUso!.UltimoUsoUtc);
    }
}
