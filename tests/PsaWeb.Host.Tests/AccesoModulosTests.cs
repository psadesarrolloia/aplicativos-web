using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PsaWeb.Host.Auth;
using PsaWeb.Identidad;
using PsaWeb.Seguridad;

namespace PsaWeb.Host.Tests;

/// <summary>
/// Guardia de los endpoints de descarga (AW-1): antes bastaba con tener sesión; sin <c>ruc</c> se exportaba la empresa fija del web.config.
/// Y la regla de 2FA obligatorio para Super Admin / Admin (AW-2).
/// </summary>
public class AccesoModulosTests
{
    private const string Sancev = "1791313747001";
    private const string Cptdc = "1792051800001";

    /// <summary>Directorio en memoria: vend tiene SANCEV con facturación; nadie tiene CPTDC.</summary>
    private sealed class DirectorioFalso : ISecurityDirectory
    {
        public Task<IReadOnlyList<EmpresaDelUsuario>> EmpresasDelUsuarioAsync(string usuario, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EmpresaDelUsuario>>(usuario == "vend" ? new[] { new EmpresaDelUsuario(Sancev, "SANCEV", 1) } : Array.Empty<EmpresaDelUsuario>());

        public Task<IReadOnlyDictionary<string, int>> ContarEmpresasAsync(IEnumerable<string> usuarios, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());

        public Task<IReadOnlySet<string>> PermisosAsync(string usuario, string ruc, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<string>>(usuario == "vend" && ruc == Sancev
                ? new HashSet<string> { Permisos.VerFacturas }
                : new HashSet<string>());

        public Task<string?> EmailUsuarioAsync(string usuario, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> EmailsPorRolAsync(string ruc, string rol = "Supervisor", CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private static ClaimsPrincipal Usuario(string nombre, string? perfil = null, bool? dosFactores = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, nombre) };
        if (perfil is not null) claims.Add(new Claim(ClaimsPsa.Perfil, perfil));
        if (dosFactores is not null) claims.Add(new Claim(ClaimsPsa.SegundoFactor, dosFactores.Value ? "1" : "0"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
    }

    private static Task<VeredictoAcceso> Evaluar(ClaimsPrincipal u, string? ruc, string app) =>
        FiltroAccesoModulo.EvaluarAsync(u, ruc, app, new DirectorioFalso(), CancellationToken.None);

    [Fact]
    public async Task Sin_empresa_no_se_exporta_nada()
    {
        Assert.Equal(VeredictoAcceso.FaltaEmpresa, await Evaluar(Usuario("vend"), null, "fe-facturas"));
        Assert.Equal(VeredictoAcceso.FaltaEmpresa, await Evaluar(Usuario("vend"), "", "fe-facturas"));
    }

    [Fact]
    public async Task Empresa_ajena_o_modulo_sin_llave_responden_denegado()
    {
        Assert.Equal(VeredictoAcceso.Denegado, await Evaluar(Usuario("vend"), Cptdc, "fe-facturas"));   // empresa no asignada
        Assert.Equal(VeredictoAcceso.Denegado, await Evaluar(Usuario("otro"), Sancev, "fe-facturas"));  // usuario sin empresas
        Assert.Equal(VeredictoAcceso.Denegado, await Evaluar(Usuario("vend"), Sancev, "fe-notas-credito")); // sin la llave del módulo
    }

    [Fact]
    public async Task Con_empresa_y_llave_se_permite()
    {
        Assert.Equal(VeredictoAcceso.Permitido, await Evaluar(Usuario("vend"), Sancev, "fe-facturas"));
    }

    [Fact]
    public async Task Super_Admin_sin_2FA_no_descarga_aunque_tenga_permiso()
    {
        Assert.Equal(VeredictoAcceso.Denegado, await Evaluar(Usuario("vend", Perfiles.SuperAdmin, dosFactores: false), Sancev, "fe-facturas"));
        Assert.Equal(VeredictoAcceso.Permitido, await Evaluar(Usuario("vend", Perfiles.SuperAdmin, dosFactores: true), Sancev, "fe-facturas"));
    }

    [Fact]
    public void ExigirModulo_rechaza_ids_inexistentes_al_arrancar()
    {
        Assert.Throws<ArgumentException>(() => AccesoModulosExtensions.ExigirModulo(null!, "no-existe"));
    }

    [Fact]
    public void DebeActivarSegundoFactor_solo_para_perfiles_con_panel_sin_2FA()
    {
        Assert.True(ClaimsPsa.DebeActivarSegundoFactor(Usuario("a", Perfiles.SuperAdmin, false)));
        Assert.True(ClaimsPsa.DebeActivarSegundoFactor(Usuario("a", Perfiles.Admin, false)));
        Assert.False(ClaimsPsa.DebeActivarSegundoFactor(Usuario("a", Perfiles.Admin, true)));
        Assert.False(ClaimsPsa.DebeActivarSegundoFactor(Usuario("a", Perfiles.Supervisor, false)));
        // Cookies emitidas antes del deploy (sin los claims nuevos): no se bloquea a nadie hasta que se renueven.
        Assert.False(ClaimsPsa.DebeActivarSegundoFactor(Usuario("a")));
        Assert.False(ClaimsPsa.DebeActivarSegundoFactor(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Theory]
    [InlineData("/mi-cuenta/seguridad", true)]
    [InlineData("/mi-cuenta/refrescar-sesion", true)]
    [InlineData("/salir", true)]
    [InlineData("/_blazor/negotiate", true)]
    [InlineData("/_framework/blazor.web.js", true)]
    [InlineData("/_content/PsaWeb.Shared/css/psa-theme.css", true)]
    [InlineData("/app.css", true)]
    [InlineData("/", false)]
    [InlineData("/kardex", false)]
    [InlineData("/ats/export", false)]
    [InlineData("/admin/usuarios", false)]
    public void Rutas_permitidas_mientras_falta_activar_el_2FA(string ruta, bool permitida)
    {
        Assert.Equal(permitida, AccesoModulosExtensions.RutaPermitidaSinSegundoFactor(new PathString(ruta)));
    }
}
