using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PsaWeb.Modules.Ventas.Prefacturas;
using PsaWeb.Notificaciones;
using PsaWeb.Seguridad;
using PsaWeb.Ventas.Prefacturas;

namespace PsaWeb.Ventas.Tests;

/// <summary>Los tests de esta colección comparten <see cref="ReglasVentas.PermisosProvisionales"/> (estático): no corren en paralelo entre sí.</summary>
[CollectionDefinition("ReglasVentas", DisableParallelization = true)]
public class ReglasVentasCollection;

[Collection("ReglasVentas")]
public class ReglasVentasTests : IDisposable
{
    private readonly bool _original = ReglasVentas.PermisosProvisionales;

    public void Dispose() => ReglasVentas.PermisosProvisionales = _original;

    private static IReadOnlySet<string> P(params string[] codigos) => new HashSet<string>(codigos);

    [Fact]
    public void Las_cuatro_llaves_nuevas_caben_en_allowAction()
    {
        foreach (var codigo in new[] { Permisos.VerInventarioVentas, Permisos.VerPrefacturas, Permisos.EmitirPrefactura, Permisos.CerrarPrefactura })
        {
            Assert.True(codigo.Length <= 10, $"{codigo} excede nvarchar(10)");
        }
        Assert.Equal(4, new[] { Permisos.VerInventarioVentas, Permisos.VerPrefacturas, Permisos.EmitirPrefactura, Permisos.CerrarPrefactura }.Distinct().Count());
    }

    [Fact]
    public void Con_las_llaves_definitivas_cada_una_abre_lo_suyo()
    {
        ReglasVentas.PermisosProvisionales = false;

        var solo_inventario = P(Permisos.VerInventarioVentas);
        Assert.True(ReglasVentas.PuedeVerInventario(solo_inventario));
        Assert.False(ReglasVentas.PuedeVerPrefacturas(solo_inventario));
        Assert.False(ReglasVentas.PuedeEmitir(solo_inventario));

        var vendedor = P(Permisos.EmitirPrefactura);
        Assert.True(ReglasVentas.PuedeEmitir(vendedor));
        Assert.True(ReglasVentas.PuedeVerPrefacturas(vendedor));   // emitir implica ver
        Assert.True(ReglasVentas.PuedeVerInventario(vendedor));    // y ver el inventario
        Assert.False(ReglasVentas.PuedeCerrar(vendedor));
        Assert.False(ReglasVentas.PuedeVerTodas(vendedor));

        var conta = P(Permisos.CerrarPrefactura);
        Assert.True(ReglasVentas.PuedeCerrar(conta));
        Assert.True(ReglasVentas.PuedeVerTodas(conta));
        Assert.True(ReglasVentas.PuedeVerPrefacturas(conta));
        Assert.False(ReglasVentas.PuedeEmitir(conta));             // cerrar no implica emitir

        var lector = P(Permisos.VerPrefacturas);
        Assert.True(ReglasVentas.PuedeVerPrefacturas(lector));
        Assert.False(ReglasVentas.PuedeEmitir(lector));
        Assert.False(ReglasVentas.PuedeCerrar(lector));
    }

    [Fact]
    public void Sin_ninguna_llave_no_se_ve_nada()
    {
        foreach (var provisional in new[] { true, false })
        {
            ReglasVentas.PermisosProvisionales = provisional;
            var nada = P("quats", "setODBC");
            Assert.False(ReglasVentas.PuedeVerInventario(nada));
            Assert.False(ReglasVentas.PuedeVerPrefacturas(nada));
            Assert.False(ReglasVentas.PuedeEmitir(nada));
            Assert.False(ReglasVentas.PuedeCerrar(nada));
        }
    }

    [Fact]
    public void Mientras_sea_provisional_la_facturacion_de_venta_habilita_el_portal()
    {
        ReglasVentas.PermisosProvisionales = true;
        var factura = P(Permisos.HacerFactura);
        Assert.True(ReglasVentas.PuedeEmitir(factura));
        Assert.True(ReglasVentas.PuedeCerrar(factura));
        Assert.True(ReglasVentas.PuedeVerPrefacturas(factura));
        var viewer = P(Permisos.VerFacturas);
        Assert.True(ReglasVentas.PuedeVerPrefacturas(viewer));
        Assert.True(ReglasVentas.PuedeVerInventario(viewer));
        Assert.False(ReglasVentas.PuedeEmitir(viewer));
        Assert.False(ReglasVentas.PuedeCerrar(viewer));

        ReglasVentas.PermisosProvisionales = false;
        Assert.False(ReglasVentas.PuedeEmitir(factura));
        Assert.False(ReglasVentas.PuedeVerPrefacturas(viewer));
    }

    [Fact]
    public void El_menu_ofrece_el_portal_solo_a_quien_tiene_alguna_llave()
    {
        ReglasVentas.PermisosProvisionales = false;
        static bool Ve(string id, params string[] permisos) =>
            AppCatalogo.Todas.Single(a => a.Id == id).VisiblePara(new ContextoDeUsuario("u", "1", new HashSet<string>(permisos)));

        Assert.False(Ve("ventas-inventario", "quats"));
        Assert.False(Ve("ventas-prefacturas", "quats"));
        Assert.True(Ve("ventas-inventario", Permisos.VerInventarioVentas));
        Assert.False(Ve("ventas-prefacturas", Permisos.VerInventarioVentas)); // solo ver inventario no abre prefacturas
        Assert.True(Ve("ventas-prefacturas", Permisos.EmitirPrefactura));
        Assert.True(Ve("ventas-prefacturas", Permisos.CerrarPrefactura));
        Assert.Equal(Categorias.Ventas, AppCatalogo.Todas.Single(a => a.Id == "ventas-prefacturas").Categoria);
    }
}

[Collection("ReglasVentas")]
public class ServicioPermisosVentasTests : IDisposable
{
    private readonly bool _original = ReglasVentas.PermisosProvisionales;

    public ServicioPermisosVentasTests() => ReglasVentas.PermisosProvisionales = false;

    public void Dispose() => ReglasVentas.PermisosProvisionales = _original;

    [Fact]
    public async Task Sin_directorio_de_seguridad_se_permite_todo_modo_sin_shell()
    {
        var servicio = new ServicioPermisosVentas(new ServiceCollection().BuildServiceProvider());
        Assert.Equal(PermisosVentas.Todos, await servicio.PermisosAsync("cualquiera", "1"));
    }

    [Fact]
    public async Task Los_permisos_salen_de_las_llaves_del_usuario_en_la_empresa()
    {
        var dir = new DirectorioFalso(new()
        {
            ["vendedor"] = new[] { Permisos.EmitirPrefactura },
            ["conta"] = new[] { Permisos.CerrarPrefactura },
            ["lector"] = new[] { Permisos.VerInventarioVentas },
        });
        var servicio = new ServicioPermisosVentas(new ServiceCollection().AddSingleton<ISecurityDirectory>(dir).BuildServiceProvider());

        Assert.Equal(new PermisosVentas(true, true, true, false), await servicio.PermisosAsync("vendedor", "1"));
        Assert.Equal(new PermisosVentas(true, true, false, true), await servicio.PermisosAsync("conta", "1"));
        Assert.Equal(new PermisosVentas(true, false, false, false), await servicio.PermisosAsync("lector", "1"));
        Assert.Equal(PermisosVentas.Ninguno, await servicio.PermisosAsync("desconocido", "1"));
    }
}

/// <summary>Los permisos se exigen en el servidor (el servicio), no solo en las pantallas.</summary>
public class ServicioPrefacturasPermisosTests
{
    private const string Ruc = "1791313747001";

    private sealed class CorreoFalso : IServicioCorreo
    {
        public bool Disponible => true;
        public int Enviados { get; private set; }
        public Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken = default) { Enviados++; return Task.CompletedTask; }
    }

    private static readonly PermisosVentas Vendedor = new(true, true, true, false);
    private static readonly PermisosVentas Contabilidad = new(true, true, false, true);
    private static readonly PermisosVentas SoloLectura = new(true, true, false, false);
    private static readonly PermisosVentas SinPermisos = PermisosVentas.Ninguno;

    private static (ServicioPrefacturas Servicio, CorreoFalso Correo) Crear()
    {
        var almacen = new AlmacenPrefacturasMemoria();
        almacen.GuardarConfiguracionAsync(ConfiguracionVentas.PorDefecto(Ruc) with { CorreoContabilidad = "conta@x.test" }, "t").Wait();
        var correo = new CorreoFalso();
        return (new ServicioPrefacturas(almacen, correo, TimeProvider.System, NullLogger<ServicioPrefacturas>.Instance), correo);
    }

    private static SolicitudPrefactura Solicitud() => PrefacturaLogicaTests.Solicitud(PrefacturaLogicaTests.Linea("A", 1, 10m));

    private static Task<ResultadoEmision> Emitir(ServicioPrefacturas s, string usuario, PermisosVentas permisos) =>
        s.EmitirAsync(Ruc, "E", Solicitud(), new ActorVentas(usuario, permisos), null);

    [Fact]
    public async Task Sin_permiso_de_emitir_no_se_guarda_ni_se_envia_nada()
    {
        var (s, correo) = Crear();
        await Assert.ThrowsAsync<AccesoDenegadoVentasException>(() => Emitir(s, "conta", Contabilidad));
        await Assert.ThrowsAsync<AccesoDenegadoVentasException>(() => Emitir(s, "lector", SoloLectura));
        await Assert.ThrowsAsync<AccesoDenegadoVentasException>(() => Emitir(s, "nadie", SinPermisos));
        Assert.Equal(0, correo.Enviados);
        Assert.Empty(await s.ListarAsync(Ruc, new FiltroPrefacturas(), new ActorVentas("conta", Contabilidad)));
    }

    [Fact]
    public async Task Un_vendedor_solo_ve_y_abre_las_suyas()
    {
        var (s, _) = Crear();
        var deAna = (await Emitir(s, "ana", Vendedor)).Prefactura!;
        var deLuis = (await Emitir(s, "luis", Vendedor)).Prefactura!;
        var ana = new ActorVentas("ana", Vendedor);

        var lista = await s.ListarAsync(Ruc, new FiltroPrefacturas(), ana);
        Assert.Equal(new[] { deAna.Id }, lista.Select(p => p.Id));
        // Aunque la pantalla pida explícitamente las de otro, se fuerza el filtro al propio usuario.
        Assert.Equal(new[] { deAna.Id }, (await s.ListarAsync(Ruc, new FiltroPrefacturas(CreadaPor: "luis"), ana)).Select(p => p.Id));

        Assert.NotNull(await s.ObtenerAsync(Ruc, deAna.Id, ana));
        Assert.Null(await s.ObtenerAsync(Ruc, deLuis.Id, ana)); // la ajena «no existe»
    }

    [Fact]
    public async Task Contabilidad_ve_todas_y_el_usuario_se_compara_sin_distinguir_mayusculas()
    {
        var (s, _) = Crear();
        await Emitir(s, "Ana", Vendedor);
        await Emitir(s, "luis", Vendedor);
        Assert.Equal(2, (await s.ListarAsync(Ruc, new FiltroPrefacturas(), new ActorVentas("conta", Contabilidad))).Count);
        Assert.Single(await s.ListarAsync(Ruc, new FiltroPrefacturas(), new ActorVentas("ANA", Vendedor)));
    }

    [Fact]
    public async Task Sin_permiso_de_ver_no_se_lista_ni_se_abre()
    {
        var (s, _) = Crear();
        var p = (await Emitir(s, "ana", Vendedor)).Prefactura!;
        var nadie = new ActorVentas("ana", SinPermisos);
        await Assert.ThrowsAsync<AccesoDenegadoVentasException>(() => s.ListarAsync(Ruc, new FiltroPrefacturas(), nadie));
        Assert.Null(await s.ObtenerAsync(Ruc, p.Id, nadie));
    }

    [Fact]
    public async Task Marcar_facturada_y_anular_son_solo_de_Contabilidad()
    {
        var (s, _) = Crear();
        var p = (await Emitir(s, "ana", Vendedor)).Prefactura!;
        var ana = new ActorVentas("ana", Vendedor);
        await Assert.ThrowsAsync<AccesoDenegadoVentasException>(() => s.MarcarFacturadaAsync(Ruc, p.Id, "001-003-000000001", ana));
        await Assert.ThrowsAsync<AccesoDenegadoVentasException>(() => s.AnularAsync(Ruc, p.Id, ana));

        var conta = new ActorVentas("conta", Contabilidad);
        await s.MarcarFacturadaAsync(Ruc, p.Id, "001-003-000000001", conta);
        var cerrada = (await s.ObtenerAsync(Ruc, p.Id, conta))!;
        Assert.Equal(EstadoPrefactura.Facturada, cerrada.Estado);
        Assert.Equal("conta", cerrada.FacturadaPor);
    }

    [Fact]
    public async Task Reenviar_el_correo_lo_hace_el_emisor_de_esa_prefactura_o_Contabilidad()
    {
        var (s, correo) = Crear();
        var p = (await Emitir(s, "ana", Vendedor)).Prefactura!;
        Assert.Equal(1, correo.Enviados);

        await s.ReenviarCorreoAsync(Ruc, p.Id, null, new ActorVentas("ana", Vendedor));
        await s.ReenviarCorreoAsync(Ruc, p.Id, null, new ActorVentas("conta", Contabilidad));
        Assert.Equal(3, correo.Enviados);

        // Otro vendedor ni siquiera ve la prefactura de Ana; un lector que la viera no puede reenviarla.
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.ReenviarCorreoAsync(Ruc, p.Id, null, new ActorVentas("luis", Vendedor)));
        await Assert.ThrowsAsync<AccesoDenegadoVentasException>(() =>
            s.ReenviarCorreoAsync(Ruc, p.Id, null, new ActorVentas("ana", new PermisosVentas(true, true, false, false))));
        Assert.Equal(3, correo.Enviados);
    }
}
