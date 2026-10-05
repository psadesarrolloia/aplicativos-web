using Microsoft.Extensions.Logging.Abstractions;
using PsaWeb.Modules.Ventas.Prefacturas;
using PsaWeb.Notificaciones;
using PsaWeb.Ventas.Prefacturas;

namespace PsaWeb.Ventas.Tests;

public class ServicioPrefacturasTests
{
    private const string Ruc = "1791313747001";

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = ahora;
        public override DateTimeOffset GetUtcNow() => Ahora;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class CorreoFalso : IServicioCorreo
    {
        public bool Disponible { get; set; } = true;
        public Exception? Falla { get; set; }
        public List<MensajeCorreo> Enviados { get; } = new();

        public Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken = default)
        {
            if (Falla is not null) throw Falla;
            Enviados.Add(mensaje);
            return Task.CompletedTask;
        }
    }

    private static (ServicioPrefacturas Servicio, AlmacenPrefacturasMemoria Almacen, CorreoFalso Correo, RelojFijo Reloj) Crear(bool conDestinatarios = true)
    {
        var almacen = new AlmacenPrefacturasMemoria();
        if (conDestinatarios)
        {
            almacen.GuardarConfiguracionAsync(ConfiguracionVentas.PorDefecto(Ruc) with { CorreoContabilidad = "conta@sancev.test", CorreoAdicional = "gerencia@sancev.test" }, "t").Wait();
        }
        var correo = new CorreoFalso();
        var reloj = new RelojFijo(new DateTimeOffset(2026, 10, 2, 15, 0, 0, TimeSpan.Zero));
        return (new ServicioPrefacturas(almacen, correo, reloj, NullLogger<ServicioPrefacturas>.Instance), almacen, correo, reloj);
    }

    private static ActorVentas Todos(string usuario = "u") => ActorVentas.ConTodos(usuario);

    private static SolicitudPrefactura Solicitud() =>
        PrefacturaLogicaTests.Solicitud(PrefacturaLogicaTests.Linea("RT18Z-32/2P EBAS", 2, 6.95m), PrefacturaLogicaTests.Linea("EBS2UZ2P1000VDC", 3, 46.51m, lista: 48.96m));

    [Fact]
    public async Task Emitir_guarda_numera_calcula_y_fija_la_vigencia_de_15_dias()
    {
        var (s, _, _, _) = Crear();
        var r = await s.EmitirAsync(Ruc, "SANCEV CIA. LTDA.", Solicitud(), Todos("vendedor1"), "https://portal/");
        var p = Assert.IsType<Prefactura>(r.Prefactura);
        Assert.Equal("PF-0001", p.NumeroTexto);
        Assert.Equal(new DateOnly(2026, 10, 2), p.FechaEmision);
        Assert.Equal(new DateOnly(2026, 10, 17), p.ValidaHasta);
        Assert.Equal(EstadoPrefactura.Emitida, p.Estado);
        Assert.Equal((153.43m, 23.01m, 176.44m), (p.Subtotal, p.Iva, p.Total));
        Assert.Equal("Net 30 Days", p.Terminos);
        Assert.Equal("4-15%", p.CodigoImpuesto);
        Assert.Equal("vendedor1", p.CreadaPor);
        Assert.Equal(2, p.Lineas.Count);
        Assert.True(p.Lineas[1].PrecioManual);
    }

    [Fact]
    public async Task La_numeracion_es_por_empresa_y_consecutiva()
    {
        var (s, _, _, _) = Crear();
        var a = (await s.EmitirAsync(Ruc, "E", Solicitud(), Todos("u"), null)).Prefactura!;
        var b = (await s.EmitirAsync(Ruc, "E", Solicitud(), Todos("u"), null)).Prefactura!;
        var otra = (await s.EmitirAsync("1792051800001", "Otra", Solicitud(), Todos("u"), null)).Prefactura!;
        Assert.Equal((1, 2, 1), (a.Numero, b.Numero, otra.Numero));
    }

    [Fact]
    public async Task Envia_un_solo_correo_a_los_dos_destinatarios_con_el_PDF_adjunto_y_los_datos_de_Sage()
    {
        var (s, _, correo, _) = Crear();
        var p = (await s.EmitirAsync(Ruc, "SANCEV CIA. LTDA.", Solicitud(), Todos("vendedor1"), "https://portal/")).Prefactura!;

        var m = Assert.Single(correo.Enviados);
        Assert.Equal(new[] { "conta@sancev.test", "gerencia@sancev.test" }, m.Para);
        Assert.Contains("PF-0001", m.Asunto);
        Assert.Contains("JACHO WILSON (EQU)", m.CuerpoHtml);
        Assert.Contains("Cliente exigente", m.CuerpoHtml); // nota interna: solo en el correo
        Assert.Contains("https://portal/ventas/prefacturas/", m.CuerpoHtml);
        var pdf = Assert.Single(m.Adjuntos!);
        Assert.EndsWith(".pdf", pdf.Nombre);
        Assert.Equal("application/pdf", pdf.TipoMime);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Contenido, 0, 4));

        Assert.Equal(EstadoCorreo.Enviado, p.CorreoEstado);
        Assert.Equal("conta@sancev.test, gerencia@sancev.test", p.CorreoDestinatarios);
        Assert.NotNull(p.CorreoEnviadoEn);
    }

    [Fact]
    public async Task Sin_destinatarios_o_sin_SMTP_la_prefactura_se_guarda_y_queda_pendiente_de_reenvio()
    {
        var (s, _, correo, _) = Crear(conDestinatarios: false);
        var p = (await s.EmitirAsync(Ruc, "E", Solicitud(), Todos("u"), null)).Prefactura!;
        Assert.Equal(EstadoCorreo.NoConfigurado, p.CorreoEstado);
        Assert.Contains("correos de Contabilidad", p.CorreoError);
        Assert.Empty(correo.Enviados);

        var (s2, _, correo2, _) = Crear();
        correo2.Disponible = false;
        var p2 = (await s2.EmitirAsync(Ruc, "E", Solicitud(), Todos("u"), null)).Prefactura!;
        Assert.Equal(EstadoCorreo.NoConfigurado, p2.CorreoEstado);
        Assert.Contains("SMTP", p2.CorreoError);
    }

    [Fact]
    public async Task Si_el_SMTP_falla_la_emision_no_se_pierde_y_se_puede_reenviar()
    {
        var (s, _, correo, _) = Crear();
        correo.Falla = new InvalidOperationException("535 autenticación rechazada");
        var p = (await s.EmitirAsync(Ruc, "E", Solicitud(), Todos("u"), null)).Prefactura!;
        Assert.Equal(EstadoCorreo.Fallo, p.CorreoEstado);
        Assert.Contains("535", p.CorreoError);
        Assert.Null(p.CorreoEnviadoEn);

        correo.Falla = null;
        var reenviada = await s.ReenviarCorreoAsync(Ruc, p.Id, null, Todos());
        Assert.Equal(EstadoCorreo.Enviado, reenviada.CorreoEstado);
        Assert.Null(reenviada.CorreoError);
        Assert.Single(correo.Enviados);
    }

    [Fact]
    public async Task Una_solicitud_invalida_no_guarda_ni_envia_nada()
    {
        var (s, almacen, correo, _) = Crear();
        var r = await s.EmitirAsync(Ruc, "E", PrefacturaLogicaTests.Solicitud(), Todos("u"), null);
        Assert.Null(r.Prefactura);
        Assert.False(r.Validacion.EsValida);
        Assert.Empty(await almacen.ListarAsync(Ruc, new FiltroPrefacturas()));
        Assert.Empty(correo.Enviados);
    }

    [Fact]
    public async Task Contabilidad_marca_la_factura_de_Sage_y_ya_no_se_puede_anular_ni_repetir()
    {
        var (s, _, _, _) = Crear();
        var p = (await s.EmitirAsync(Ruc, "E", Solicitud(), Todos("u"), null)).Prefactura!;
        await s.MarcarFacturadaAsync(Ruc, p.Id, " 001-003-000013539 ", Todos("contadora"));

        var f = (await s.ObtenerAsync(Ruc, p.Id, Todos()))!;
        Assert.Equal(EstadoPrefactura.Facturada, f.Estado);
        Assert.Equal("001-003-000013539", f.FacturaSage);
        Assert.Equal("contadora", f.FacturadaPor);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.AnularAsync(Ruc, p.Id, Todos()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.MarcarFacturadaAsync(Ruc, p.Id, "otra", Todos("x")));
        await Assert.ThrowsAsync<ArgumentException>(() => s.MarcarFacturadaAsync(Ruc, p.Id, "  ", Todos("x")));
    }

    [Fact]
    public async Task Anular_solo_sirve_para_prefacturas_emitidas()
    {
        var (s, _, _, _) = Crear();
        var p = (await s.EmitirAsync(Ruc, "E", Solicitud(), Todos("u"), null)).Prefactura!;
        await s.AnularAsync(Ruc, p.Id, Todos());
        Assert.Equal(EstadoPrefactura.Anulada, (await s.ObtenerAsync(Ruc, p.Id, Todos()))!.Estado);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.MarcarFacturadaAsync(Ruc, p.Id, "x", Todos("u")));
    }

    [Fact]
    public async Task Vence_a_los_15_dias_y_se_reporta_como_vencida()
    {
        var (s, _, _, reloj) = Crear();
        var p = (await s.EmitirAsync(Ruc, "E", Solicitud(), Todos("u"), null)).Prefactura!;
        Assert.Equal("Vigente", p.EstadoVisible(new DateOnly(2026, 10, 17)));
        Assert.Equal("Vencida", p.EstadoVisible(new DateOnly(2026, 10, 18)));
        reloj.Ahora = reloj.Ahora.AddDays(20);
        Assert.Equal(new DateOnly(2026, 10, 22), s.Hoy);
        Assert.True(p.EstaVencida(s.Hoy));
    }

    [Fact]
    public async Task Una_empresa_no_ve_las_prefacturas_de_otra()
    {
        var (s, _, _, _) = Crear();
        var p = (await s.EmitirAsync(Ruc, "E", Solicitud(), Todos("u"), null)).Prefactura!;
        Assert.Null(await s.ObtenerAsync("1792051800001", p.Id, Todos()));
        Assert.Empty(await s.ListarAsync("1792051800001", new FiltroPrefacturas(), Todos()));
    }

    [Fact]
    public async Task La_lista_filtra_por_creador_estado_y_texto()
    {
        var (s, _, _, _) = Crear();
        await s.EmitirAsync(Ruc, "E", Solicitud(), Todos("ana"), null);
        var b = (await s.EmitirAsync(Ruc, "E", Solicitud(), Todos("luis"), null)).Prefactura!;
        await s.AnularAsync(Ruc, b.Id, Todos());

        Assert.Single(await s.ListarAsync(Ruc, new FiltroPrefacturas(CreadaPor: "ana"), Todos()));
        Assert.Single(await s.ListarAsync(Ruc, new FiltroPrefacturas(Estado: EstadoPrefactura.Anulada), Todos()));
        Assert.Equal(2, (await s.ListarAsync(Ruc, new FiltroPrefacturas(Texto: "carlota"), Todos())).Count);
        Assert.Empty(await s.ListarAsync(Ruc, new FiltroPrefacturas(Texto: "no existe"), Todos()));
    }
}
