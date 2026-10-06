using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PsaWeb.Identidad;
using PsaWeb.PeachEbills.Data;
using PsaWeb.Seguridad;

namespace PsaWeb.Seguridad.Tests;

/// <summary>
/// Directorio de accesos web + servicio del panel contra las bases locales de PREDATOR (<c>PsaWebPlataforma</c> y <c>PeachEBills</c>
/// en .\SQLEXPRESS). Aplica las migraciones de PsaWebPlataforma (base de desarrollo). Crea cuentas <c>t_aw_*</c> y las borra al final.
/// Se saltea si las bases no están.
/// </summary>
public sealed class AccesosWebIntegracionTests : IAsyncLifetime
{
    private const string Plataforma =
        @"Server=.\SQLEXPRESS;Database=PsaWebPlataforma;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15";
    private const string Peach =
        @"Server=.\SQLEXPRESS;Database=PeachEBills;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15";
    private const string Sancev = "1791313747001";
    private const string ClaveValida = "Psa.Web.2026!seg";

    private sealed class FactoryPeach : IDbContextFactory<PeachEbillsContext>
    {
        private readonly DbContextOptions<PeachEbillsContext> _o = new DbContextOptionsBuilder<PeachEbillsContext>().UseSqlServer(Peach).Options;
        public PeachEbillsContext CreateDbContext() => new(_o);
    }

    private readonly ServiceProvider _sp;
    private readonly string _sufijo = Guid.NewGuid().ToString("N")[..8];
    private readonly List<string> _nombres = new();
    private bool _disponible;

    public AccesosWebIntegracionTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Plataforma:ConnectionString"] = Plataforma })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddIdentidadPlataforma(config);
        services.AddSingleton<IDbContextFactory<PeachEbillsContext>>(new FactoryPeach());
        services.AddScoped<PeachEbillsSecurityDirectory>();
        services.AddScoped<ICatalogoEmpresas, CatalogoEmpresasPeachEbills>();
        services.AddScoped<AccesosWebSecurityDirectory>();
        services.AddScoped<ServicioAccesos>();
        _sp = services.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var peach = new FactoryPeach().CreateDbContext();
            await using var scope = _sp.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PlataformaDbContext>();
            _disponible = await peach.Database.CanConnectAsync() && await db.Database.CanConnectAsync();
            if (_disponible) await db.Database.MigrateAsync();
        }
        catch
        {
            _disponible = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_disponible)
        {
            await using var scope = _sp.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<UsuarioApp>>();
            foreach (var n in _nombres)
            {
                if (await users.FindByNameAsync(n) is { } u) await users.DeleteAsync(u); // los accesos se borran en cascada
            }
            var db = scope.ServiceProvider.GetRequiredService<PlataformaDbContext>();
            var patron = "t_aw_" + _sufijo;
            db.EventosAuth.RemoveRange(db.EventosAuth.Where(e => e.Usuario.Contains(patron) || (e.Detalle != null && e.Detalle.Contains(patron))));
            await db.SaveChangesAsync();
        }
        await _sp.DisposeAsync();
    }

    private async Task<UsuarioApp> CrearAsync(UserManager<UsuarioApp> users, string rol, string perfil)
    {
        var nombre = $"t_aw_{_sufijo}_{rol}";
        _nombres.Add(nombre);
        var u = new UsuarioApp { UserName = nombre, Email = $"{nombre}@prueba.paredes.com.ec", Perfil = perfil, NombreCompleto = nombre, PeachUsername = nombre };
        var r = await users.CreateAsync(u, ClaveValida);
        Assert.True(r.Succeeded, string.Join("; ", r.Errors.Select(e => e.Description)));
        return u;
    }

    [SkippableFact]
    public async Task Recorrido_completo_del_panel_y_del_directorio()
    {
        Skip.IfNot(_disponible, "PsaWebPlataforma / PeachEBills locales no disponibles.");
        await using var scope = _sp.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var users = sp.GetRequiredService<UserManager<UsuarioApp>>();
        var servicio = sp.GetRequiredService<ServicioAccesos>();
        var dir = sp.GetRequiredService<AccesosWebSecurityDirectory>();
        var auditoria = sp.GetRequiredService<AuditoriaAuth>();

        var super = await CrearAsync(users, "super", Perfiles.SuperAdmin);
        var admin = await CrearAsync(users, "admin", Perfiles.Admin);
        var vend = await CrearAsync(users, "vend", Perfiles.Usuario);

        var actorSuper = (await servicio.ActorAsync(super.UserName))!;
        var actorAdmin = (await servicio.ActorAsync(admin.UserName))!;
        Assert.Equal(RolPanel.SuperAdmin, actorSuper.Rol);
        Assert.Equal(RolPanel.Admin, actorAdmin.Rol);
        Assert.Null(await servicio.ActorAsync("no-existe-" + _sufijo));

        // Sin filas, no ve nada (fuente Web: cerrado salvo que se marque).
        Assert.Empty(await dir.EmpresasDelUsuarioAsync(vend.UserName!));

        // Vendedor en SANCEV.
        var vendedor = LlavesWeb.Plantillas["Vendedor"];
        await servicio.GuardarEmpresaAsync(actorSuper, vend.Id, Sancev, true, "VENDEDOR 1", vendedor);
        Assert.Contains(await dir.EmpresasDelUsuarioAsync(vend.UserName!), e => e.Ruc == Sancev && !e.Nombre.Contains('\n'));
        Assert.Equal(vendedor.ToHashSet(), (await dir.PermisosAsync(vend.UserName!, Sancev)).ToHashSet());
        Assert.Empty(await dir.PermisosAsync(vend.UserName!, "1792051800001")); // otra empresa: nada
        Assert.Equal(1, (await dir.ContarEmpresasAsync(new[] { vend.UserName! }))[vend.UserName!]);

        // AW-5: el usuario de Sage de esa empresa habilita escribir; en otra empresa (o sin cargar) no.
        var vinculo = await dir.VinculoSageAsync(vend.UserName!, Sancev);
        Assert.True(vinculo.Exigido);
        Assert.Equal("VENDEDOR 1", vinculo.UsuarioSage);
        Assert.True(vinculo.PermiteEscribir);
        Assert.False((await dir.VinculoSageAsync(vend.UserName!, "1792051800001")).PermiteEscribir);

        // Desactivar la empresa corta el acceso pero conserva las llaves; reactivar las devuelve.
        await servicio.GuardarEmpresaAsync(actorSuper, vend.Id, Sancev, false, "VENDEDOR 1", vendedor);
        Assert.Empty(await dir.EmpresasDelUsuarioAsync(vend.UserName!));
        Assert.Empty(await dir.PermisosAsync(vend.UserName!, Sancev));
        var ficha = (await servicio.FichaAsync(vend.Id)).Single(f => f.Ruc == Sancev);
        Assert.True(ficha.Asignada);
        Assert.False(ficha.Activo);
        Assert.Equal(vendedor.ToHashSet(), ficha.Llaves.ToHashSet());
        Assert.Equal("VENDEDOR 1", ficha.UsuarioSage);
        await servicio.GuardarEmpresaAsync(actorSuper, vend.Id, Sancev, true, "VENDEDOR 1", vendedor);
        Assert.NotEmpty(await dir.PermisosAsync(vend.UserName!, Sancev));

        // Llaves desconocidas: rechazadas.
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            servicio.GuardarEmpresaAsync(actorSuper, vend.Id, Sancev, true, null, new[] { "inventada" }));

        // Admin: edita al vendedor, no al Super Admin ni a sí misma; no crea usuarios.
        await servicio.GuardarEmpresaAsync(actorAdmin, vend.Id, Sancev, true, "VENDEDOR 1", vendedor.Append(Permisos.VerKardex));
        Assert.Contains(Permisos.VerKardex, await dir.PermisosAsync(vend.UserName!, Sancev));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            servicio.GuardarEmpresaAsync(actorAdmin, super.Id, Sancev, true, null, vendedor));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            servicio.GuardarEmpresaAsync(actorAdmin, admin.Id, Sancev, true, null, vendedor));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            servicio.CrearUsuarioAsync(actorAdmin, "t_aw_x" + _sufijo, "X", "x@x.ec", Perfiles.Usuario, null));

        // Correo único: no se puede crear otra cuenta con el mismo correo.
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            servicio.CrearUsuarioAsync(actorSuper, "t_aw_" + _sufijo + "_dup", "Dup", vend.Email!, Perfiles.Usuario, null));

        // Supervisor (anulaciones) = quien tiene llaves auCance* en esa empresa.
        await servicio.GuardarEmpresaAsync(actorSuper, admin.Id, Sancev, true, null, new[] { Permisos.AutorizarAnulacionFactura });
        Assert.Contains(admin.Email!, await dir.EmailsPorRolAsync(Sancev));

        // Cuenta deshabilitada: sin empresas ni permisos.
        await servicio.CambiarActivoAsync(actorSuper, vend.Id, false);
        Assert.Empty(await dir.EmpresasDelUsuarioAsync(vend.UserName!));
        Assert.Empty(await dir.PermisosAsync(vend.UserName!, Sancev));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => servicio.CambiarActivoAsync(actorSuper, super.Id, false)); // a sí mismo no

        // Auditoría: quién, a quién y qué.
        var eventos = await auditoria.BuscarAsync(super.UserName, TiposEventoAuth.AccesoOtorgado);
        Assert.Contains(eventos, e => e.Detalle!.Contains(vend.UserName!) && e.Detalle.Contains(Permisos.EmitirPrefactura));
        Assert.Contains(await auditoria.BuscarAsync(admin.UserName, TiposEventoAuth.AccesoOtorgado), e => e.Detalle!.Contains(Permisos.VerKardex));
    }

    [SkippableFact]
    public async Task Importar_del_exe_copia_solo_llaves_web_de_empresas_activas_y_no_quita_nada()
    {
        Skip.IfNot(_disponible, "PsaWebPlataforma / PeachEBills locales no disponibles.");
        await using var scope = _sp.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var users = sp.GetRequiredService<UserManager<UsuarioApp>>();
        var servicio = sp.GetRequiredService<ServicioAccesos>();
        var dir = sp.GetRequiredService<AccesosWebSecurityDirectory>();
        var peach = sp.GetRequiredService<PeachEbillsSecurityDirectory>();
        var catalogo = sp.GetRequiredService<ICatalogoEmpresas>();

        var super = await CrearAsync(users, "super2", Perfiles.SuperAdmin);
        var dig = await CrearAsync(users, "dig", Perfiles.Usuario);
        var actor = (await servicio.ActorAsync(super.UserName))!;

        // Algo propio de la web que no está en el .exe: no se tiene que perder.
        await servicio.GuardarEmpresaAsync(actor, dig.Id, Sancev, true, null, new[] { Permisos.VerKardex });

        var n = await servicio.ImportarDesdePeachEbillsAsync(actor, dig.Id, "lparedes"); // usuario real de la copia de PeachEBills de PREDATOR
        var activas = (await catalogo.TodasAsync()).Where(e => e.Activa).Select(e => e.Ruc).ToHashSet();
        var esperadas = (await peach.EmpresasDelUsuarioAsync("lparedes")).Where(e => activas.Contains(e.Ruc)).ToList();
        Assert.Equal(esperadas.Count, n);

        foreach (var e in esperadas)
        {
            var web = await dir.PermisosAsync(dig.UserName!, e.Ruc);
            var exe = (await peach.PermisosAsync("lparedes", e.Ruc)).Where(LlavesWeb.Existe).ToHashSet();
            Assert.Superset(exe, web.ToHashSet());
            Assert.All(web, l => Assert.True(LlavesWeb.Existe(l)));
        }
        if (esperadas.Any(e => e.Ruc == Sancev))
        {
            Assert.Contains(Permisos.VerKardex, await dir.PermisosAsync(dig.UserName!, Sancev));
        }
    }
}
