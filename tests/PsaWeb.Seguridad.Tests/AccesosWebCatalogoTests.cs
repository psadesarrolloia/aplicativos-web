using Microsoft.Extensions.Configuration;
using PsaWeb.Identidad;
using PsaWeb.Seguridad;

namespace PsaWeb.Seguridad.Tests;

/// <summary>
/// Catálogo de módulos con y sin GateProvisional (PLAN-ACCESOS-WEB §4.1), resolución ruta → módulo de la guardia central, llaves del
/// panel y plantillas. Usa el modo explícito de <see cref="AppWeb.VisiblePara(ContextoDeUsuario, bool)"/>: el modo global es estado compartido.
/// </summary>
public class AccesosWebCatalogoTests
{
    private static ContextoDeUsuario Con(params string[] llaves) => new("u", "1791313747001", llaves.ToHashSet());

    private static AppWeb App(string id) => AppCatalogo.PorId(id)!;

    [Theory]
    [InlineData("kardex")]
    [InlineData("ats")]
    [InlineData("conciliacion-sri")]
    [InlineData("cierre-de-caja")]
    [InlineData("reporte-pwc")]
    [InlineData("reporte-comisiones")]
    [InlineData("cheques")]
    public void Provisional_abre_los_modulos_sin_llave_cargada_y_estricto_los_cierra(string id)
    {
        Assert.True(App(id).VisiblePara(Con(), modoProvisional: true));   // como hoy en producción (fuente PeachEBills)
        Assert.False(App(id).VisiblePara(Con(), modoProvisional: false)); // fuente Web: cerrado salvo que se marque
    }

    [Theory]
    [InlineData("kardex", Permisos.VerKardex)]
    [InlineData("ats", Permisos.VerAts)]
    [InlineData("conciliacion-sri", Permisos.VerConciliacionSri)]
    [InlineData("cierre-de-caja", Permisos.VerCierreCaja)]
    [InlineData("cheques", Permisos.VerReporteCheques)]
    public void Estricto_abre_con_su_llave(string id, string llave)
    {
        Assert.True(App(id).VisiblePara(Con(llave), modoProvisional: false));
    }

    [Fact]
    public void Ventas_estricto_no_se_abre_con_las_llaves_de_facturacion_electronica()
    {
        Assert.True(App("ventas-inventario").VisiblePara(Con(Permisos.VerFacturas), modoProvisional: true));
        Assert.False(App("ventas-inventario").VisiblePara(Con(Permisos.VerFacturas), modoProvisional: false));
        Assert.False(App("ventas-prefacturas").VisiblePara(Con(Permisos.HacerFactura), modoProvisional: false));
        Assert.True(App("ventas-prefacturas").VisiblePara(Con(Permisos.EmitirPrefactura), modoProvisional: false));
        // Solo inventario no da prefacturas.
        Assert.False(App("ventas-prefacturas").VisiblePara(Con(Permisos.VerInventarioVentas), modoProvisional: false));
    }

    [Fact]
    public void Compras_estricto_no_se_abre_con_las_llaves_de_retenciones()
    {
        Assert.True(App("compras").VisiblePara(Con(Permisos.HacerRetencion), modoProvisional: true));
        Assert.False(App("compras").VisiblePara(Con(Permisos.HacerRetencion), modoProvisional: false));
        Assert.False(App("compras-importaciones").VisiblePara(Con(Permisos.RegistrarCompras), modoProvisional: false));
    }

    [Fact]
    public void Los_modulos_sin_regimen_provisional_no_cambian_entre_modos()
    {
        foreach (var app in AppCatalogo.Todas.Where(a => a.PermisosProvisionales is null))
        {
            Assert.False(app.VisiblePara(Con(), true));
            Assert.False(app.VisiblePara(Con(), false));
            Assert.True(app.VisiblePara(Con(app.PermisosQueLaHabilitan[0]), true));
            Assert.True(app.VisiblePara(Con(app.PermisosQueLaHabilitan[0]), false));
        }
    }

    [Fact]
    public void Ningun_modulo_queda_abierto_a_cualquiera_en_modo_estricto()
    {
        Assert.All(AppCatalogo.Todas, a =>
        {
            Assert.NotEmpty(a.PermisosQueLaHabilitan);
            Assert.False(a.VisiblePara(Con(), modoProvisional: false));
        });
    }

    [Theory]
    [InlineData("/kardex", "kardex")]
    [InlineData("/Kardex/", "kardex")]
    [InlineData("/kardex?x=1", "kardex")]
    [InlineData("/compras", "compras")]
    [InlineData("/compras/nueva", "compras")]
    [InlineData("/compras/123/copiar", "compras")]
    [InlineData("/compras/recibidos", "compras-recibidos")]
    [InlineData("/compras/recibidos/factura/0101", "compras-recibidos")]
    [InlineData("/compras/importaciones/1105", "compras-importaciones")]
    [InlineData("/exportar/liquidacion-importacion", "compras-importaciones")]
    [InlineData("/retenciones", "retenciones")]
    [InlineData("/fe/retenciones", "retenciones")]
    [InlineData("/ventas/prefacturas/nueva", "ventas-prefacturas")]
    [InlineData("/ventas/prefacturas/12", "ventas-prefacturas")]
    [InlineData("/ventas/inventario", "ventas-inventario")]
    [InlineData("ats", "ats")]
    public void ModuloDeRuta_resuelve_la_ruta_y_sus_subrutas_ganando_la_mas_larga(string ruta, string esperado)
    {
        Assert.Equal(esperado, AppCatalogo.ModuloDeRuta(ruta)?.Id);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("/admin/usuarios")]
    [InlineData("/mi-cuenta/seguridad")]
    [InlineData("/seleccionar-empresa")]
    [InlineData("/kardexx")]
    [InlineData("/comprasx/1")]
    public void ModuloDeRuta_no_inventa_modulos(string? ruta)
    {
        Assert.Null(AppCatalogo.ModuloDeRuta(ruta));
    }

    [Fact]
    public void Toda_llave_que_exige_un_modulo_se_puede_otorgar_desde_el_panel()
    {
        foreach (var llave in AppCatalogo.Todas.SelectMany(a => a.PermisosQueLaHabilitan))
        {
            Assert.True(LlavesWeb.Existe(llave), $"La llave {llave} no está en LlavesWeb");
        }
    }

    [Fact]
    public void Las_llaves_del_panel_caben_en_allowAction_y_pertenecen_a_un_modulo()
    {
        Assert.All(LlavesWeb.Todas, l =>
        {
            Assert.InRange(l.Codigo.Length, 1, 10); // allowCode = nvarchar(10), AccesosLlave.Llave = nvarchar(10)
            Assert.NotNull(AppCatalogo.PorId(l.AppId));
        });
        Assert.Equal(LlavesWeb.Todas.Count, LlavesWeb.Todas.Select(l => l.Codigo).Distinct().Count());
    }

    [Fact]
    public void Las_plantillas_solo_usan_llaves_existentes()
    {
        foreach (var (nombre, llaves) in LlavesWeb.Plantillas)
        {
            Assert.All(llaves, l => Assert.True(LlavesWeb.Existe(l), $"Plantilla {nombre}: {l}"));
        }
        Assert.Equal(LlavesWeb.Todas.Count, LlavesWeb.Plantillas["Super Admin / Admin (todo)"].Count);
    }

    [Fact]
    public void La_plantilla_de_vendedor_da_inventario_y_prefacturas_propias_pero_no_contabilidad()
    {
        var ctx = Con(LlavesWeb.Plantillas["Vendedor"].ToArray());
        Assert.True(App("ventas-inventario").VisiblePara(ctx, false));
        Assert.True(App("ventas-prefacturas").VisiblePara(ctx, false));
        Assert.False(ctx.Puede(Permisos.CerrarPrefactura));
        Assert.False(App("fe-facturas").VisiblePara(ctx, false));
        Assert.False(App("kardex").VisiblePara(ctx, false));
    }

    [Fact]
    public void La_plantilla_de_digitador_es_la_del_rol_del_exe_mas_conciliacion()
    {
        var ctx = Con(LlavesWeb.Plantillas["Digitador/a"].ToArray());
        foreach (var id in new[] { "fe-facturas", "fe-notas-credito", "fe-liquidaciones", "retenciones", "reporte-pwc", "reporte-comisiones", "cheques", "conciliacion-sri" })
        {
            Assert.True(App(id).VisiblePara(ctx, false), id);
        }
        Assert.False(ctx.Puede(Permisos.HacerFacturasLote));
        Assert.False(ctx.Puede(Permisos.AutorizarAnulacionFactura));
        Assert.False(App("ats").VisiblePara(ctx, false));
    }

    [Fact]
    public void LeerFuente_por_defecto_es_PeachEBills_y_acepta_Web_sin_distinguir_mayusculas()
    {
        Assert.Equal(FuenteAccesos.PeachEBills, ServiceCollectionExtensions.LeerFuente(null));
        var cfg = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Accesos:Fuente"] = "web" }).Build();
        Assert.Equal(FuenteAccesos.Web, ServiceCollectionExtensions.LeerFuente(cfg));
        var mal = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Accesos:Fuente"] = "cualquiercosa" }).Build();
        Assert.Equal(FuenteAccesos.PeachEBills, ServiceCollectionExtensions.LeerFuente(mal));
    }
}

/// <summary>Quién puede hacer qué en el panel (PLAN-ACCESOS-WEB §2 y §4.2).</summary>
public class ReglasPanelTests
{
    [Fact]
    public void Super_Admin_edita_a_cualquiera_incluso_a_si_mismo()
    {
        Assert.True(ReglasPanel.PuedeEditarAccesos(RolPanel.SuperAdmin, "a", Perfiles.SuperAdmin, "b"));
        Assert.True(ReglasPanel.PuedeEditarAccesos(RolPanel.SuperAdmin, "a", Perfiles.Digitador, "a"));
    }

    [Fact]
    public void Admin_no_toca_Super_Admin_ni_su_propia_cuenta()
    {
        Assert.False(ReglasPanel.PuedeEditarAccesos(RolPanel.Admin, "m", Perfiles.SuperAdmin, "s"));
        Assert.False(ReglasPanel.PuedeEditarAccesos(RolPanel.Admin, "m", Perfiles.Admin, "m"));
        Assert.True(ReglasPanel.PuedeEditarAccesos(RolPanel.Admin, "m", Perfiles.Digitador, "v"));
    }

    [Fact]
    public void Un_usuario_comun_no_edita_nada()
    {
        Assert.False(ReglasPanel.PuedeEditarAccesos(RolPanel.Ninguno, "u", Perfiles.Digitador, "v"));
        Assert.False(ReglasPanel.PuedeVerAuditoria(RolPanel.Ninguno));
    }

    [Fact]
    public void Solo_Super_Admin_administra_usuarios_y_perfiles_y_nadie_cambia_su_propio_perfil()
    {
        Assert.True(ReglasPanel.PuedeAdministrarUsuarios(RolPanel.SuperAdmin));
        Assert.False(ReglasPanel.PuedeAdministrarUsuarios(RolPanel.Admin));
        Assert.True(ReglasPanel.PuedeCambiarPerfil(RolPanel.SuperAdmin, "a", "b", Perfiles.Digitador, Perfiles.Admin));
        Assert.False(ReglasPanel.PuedeCambiarPerfil(RolPanel.SuperAdmin, "a", "a", Perfiles.SuperAdmin, Perfiles.Digitador));
        Assert.False(ReglasPanel.PuedeCambiarPerfil(RolPanel.Ninguno, "x", "v", Perfiles.Digitador, Perfiles.Vendedor));
    }

    [Fact]
    public void Admin_cambia_perfiles_sin_panel_pero_no_da_ni_quita_Admin_o_Super_Admin()
    {
        Assert.True(ReglasPanel.PuedeCambiarPerfil(RolPanel.Admin, "m", "v", Perfiles.Digitador, Perfiles.Supervisor));
        Assert.True(ReglasPanel.PuedeCambiarPerfil(RolPanel.Admin, "m", "v", Perfiles.Vendedor, Perfiles.Consulta));
        Assert.False(ReglasPanel.PuedeCambiarPerfil(RolPanel.Admin, "m", "v", Perfiles.Digitador, Perfiles.Admin));
        Assert.False(ReglasPanel.PuedeCambiarPerfil(RolPanel.Admin, "m", "s", Perfiles.SuperAdmin, Perfiles.Digitador));
        Assert.False(ReglasPanel.PuedeCambiarPerfil(RolPanel.Admin, "m", "m", Perfiles.Admin, Perfiles.Admin));
        Assert.DoesNotContain(Perfiles.Admin, ReglasPanel.PerfilesAsignables(RolPanel.Admin));
        Assert.Equal(Perfiles.Todos, ReglasPanel.PerfilesAsignables(RolPanel.SuperAdmin));
    }

    [Fact]
    public void Plataforma_Admins_del_web_config_cuenta_como_Super_Admin()
    {
        var opciones = new PlataformaOptions { Admins = { "lparedes" } };
        Assert.Equal(Perfiles.SuperAdmin, UsuarioClaimsFactory.PerfilEfectivo(new UsuarioApp { UserName = "LPAREDES", Perfil = Perfiles.Digitador }, opciones));
        Assert.Equal(Perfiles.Admin, UsuarioClaimsFactory.PerfilEfectivo(new UsuarioApp { UserName = "mmartinez", Perfil = Perfiles.Admin }, opciones));
        Assert.Equal(Perfiles.Consulta, UsuarioClaimsFactory.PerfilEfectivo(new UsuarioApp { UserName = "x", Perfil = "inventado" }, opciones));
    }

    [Fact]
    public void Super_Admin_y_Admin_exigen_segundo_factor_el_resto_no()
    {
        Assert.True(Perfiles.ExigeSegundoFactor(Perfiles.SuperAdmin));
        Assert.True(Perfiles.ExigeSegundoFactor(Perfiles.Admin));
        Assert.False(Perfiles.ExigeSegundoFactor(Perfiles.Supervisor));
        Assert.False(Perfiles.ExigeSegundoFactor(Perfiles.Digitador));
        Assert.False(Perfiles.ExigeSegundoFactor(Perfiles.Vendedor));
        Assert.False(Perfiles.ExigeSegundoFactor(null));
    }

    [Theory]
    [InlineData("contadora@empresa.com.ec", true)]
    [InlineData("  a.b@c.ec ", true)]
    [InlineData("sin-arroba", false)]
    [InlineData("a@b", false)]
    [InlineData("Nombre <a@b.ec>", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EsCorreo(string? texto, bool esperado) => Assert.Equal(esperado, ServicioAccesos.EsCorreo(texto));
}

/// <summary>Vínculo con el usuario de Sage (AW-5): con la fuente PeachEBills no se exige; con Web, sin usuario de Sage no se escribe.</summary>
public class VinculoSageTests
{
    [Fact]
    public void Con_PeachEBills_no_se_exige_y_no_bloquea()
    {
        Assert.False(VinculoSage.NoAplica.Exigido);
        Assert.True(VinculoSage.NoAplica.PermiteEscribir);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("MONICA MARTINEZ", true)]
    public void Con_la_fuente_Web_hace_falta_el_usuario_de_Sage(string? usuarioSage, bool permite)
    {
        Assert.Equal(permite, new VinculoSage(true, usuarioSage).PermiteEscribir);
    }

    [Fact]
    public async Task El_directorio_por_defecto_no_aplica_el_vinculo()
    {
        ISecurityDirectory dir = new DirectorioMinimo();
        Assert.Equal(VinculoSage.NoAplica, await dir.VinculoSageAsync("u", "1791313747001"));
    }

    [Fact]
    public void La_auditoria_lleva_el_usuario_de_Sage_si_lo_hay()
    {
        Assert.Equal("mmartinez (Sage: MONICA MARTINEZ)", MensajesEscrituraSage.ConUsuarioSage("mmartinez", "MONICA MARTINEZ"));
        Assert.Equal("lparedes", MensajesEscrituraSage.ConUsuarioSage("lparedes", null));
    }

    [Fact]
    public void Usuarios_de_Sage_compara_sin_distinguir_mayusculas_ni_espacios()
    {
        var us = new UsuariosSage(new[] { "ASISTENTE-1", "MONICA MARTINEZ" }, null);
        Assert.True(us.Contiene(" monica martinez "));
        Assert.False(us.Contiene("NOEXISTE"));
        Assert.False(us.Contiene(null));
    }

    private sealed class DirectorioMinimo : ISecurityDirectory
    {
        public Task<IReadOnlyList<EmpresaDelUsuario>> EmpresasDelUsuarioAsync(string usuario, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<string, int>> ContarEmpresasAsync(IEnumerable<string> usuarios, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlySet<string>> PermisosAsync(string usuario, string ruc, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string?> EmailUsuarioAsync(string usuario, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> EmailsPorRolAsync(string ruc, string rol = "Supervisor", CancellationToken ct = default) => throw new NotImplementedException();
    }
}

/// <summary>Perfil único (2026-10-06): módulos por defecto, «personalizado» y la deducción que replica la migración PerfilUnico.</summary>
public class PerfilesWebTests
{
    [Theory]
    [InlineData(Perfiles.SuperAdmin, PerfilesWeb.PlantillaTodo)]
    [InlineData(Perfiles.Admin, PerfilesWeb.PlantillaTodo)]
    [InlineData(Perfiles.Supervisor, "Supervisor")]
    [InlineData(Perfiles.Digitador, "Digitador/a")]
    [InlineData(Perfiles.Vendedor, "Vendedor")]
    public void Cada_perfil_trae_los_modulos_de_su_plantilla(string perfil, string plantilla)
    {
        Assert.Equal(LlavesWeb.Plantillas[plantilla], PerfilesWeb.LlavesPorDefecto(perfil));
        Assert.Equal(plantilla, PerfilesWeb.PlantillaDe(perfil));
    }

    [Fact]
    public void Consulta_no_trae_modulos_y_el_perfil_heredado_no_tiene_plantilla()
    {
        Assert.Empty(PerfilesWeb.LlavesPorDefecto(Perfiles.Consulta)!);
        Assert.Null(PerfilesWeb.LlavesPorDefecto(Perfiles.Usuario));
    }

    [Fact]
    public void Personalizado_si_alguna_empresa_se_aparta_del_perfil()
    {
        var dig = LlavesWeb.Plantillas["Digitador/a"];
        Assert.False(PerfilesWeb.Personalizado(Perfiles.Digitador, new[] { dig, dig }));
        Assert.True(PerfilesWeb.Personalizado(Perfiles.Digitador, new[] { dig, dig.Append(Permisos.VerKardex) }));
        Assert.True(PerfilesWeb.Personalizado(Perfiles.Digitador, new[] { dig.Take(3) }));
        Assert.False(PerfilesWeb.Personalizado(Perfiles.Consulta, new[] { new[] { Permisos.VerKardex } })); // Consulta: siempre a mano
        Assert.False(PerfilesWeb.Personalizado(Perfiles.Vendedor, Array.Empty<IEnumerable<string>>()));
    }

    [Fact]
    public void Deducir_replica_la_regla_de_la_migracion()
    {
        Assert.Equal(Perfiles.Supervisor, PerfilesWeb.Deducir(LlavesWeb.Plantillas["Supervisor"]));
        Assert.Equal(Perfiles.Supervisor, PerfilesWeb.Deducir(new[] { Permisos.VerFacturas, Permisos.AutorizarAnulacionRetencion }));
        Assert.Equal(Perfiles.Digitador, PerfilesWeb.Deducir(LlavesWeb.Plantillas["Digitador/a"]));
        Assert.Equal(Perfiles.Vendedor, PerfilesWeb.Deducir(LlavesWeb.Plantillas["Vendedor"]));
        Assert.Equal(Perfiles.Consulta, PerfilesWeb.Deducir(new[] { Permisos.VerKardex, Permisos.VerAts }));
        Assert.Equal(Perfiles.Consulta, PerfilesWeb.Deducir(Array.Empty<string>()));
    }

    [Fact]
    public void La_migracion_PerfilUnico_usa_las_mismas_llaves_que_Deducir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PsaWeb.sln"))) dir = dir.Parent;
        var archivo = Directory.GetFiles(Path.Combine(dir!.FullName, "src", "PsaWeb.Identidad", "Migrations"), "*_PerfilUnico.cs").Single();
        var sql = File.ReadAllText(archivo);
        foreach (var llave in new[] { "auCanceInv", "auCanceNc", "auCanceLiq", "auCanceTwh", "mksaleinv", "mksalenc", "mkpurchliq", "mkpurchtwh",
                                      "mksinBatch", "mkncBatch", "mkliqBatch", "mkTwhBatch", "mkpurchinv", "mkimpliq", "quSalesStk", "quSalesQte", "mkSalesQte" })
        {
            Assert.Contains($"'{llave}'", sql);
        }
    }
}
