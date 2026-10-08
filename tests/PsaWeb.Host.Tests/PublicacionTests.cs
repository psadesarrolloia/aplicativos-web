using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PsaWeb.Host.Auth;

namespace PsaWeb.Host.Tests;

/// <summary>
/// E1 del acceso externo: el sitio detrás de Cloudflare Tunnel. Se prueba el pipeline real (TestServer) con la configuración del web.config.
/// </summary>
public class PublicacionTests
{
    private const string Conector = "192.168.0.50";      // PC con cloudflared
    private const string Oficina = "200.1.2.3";          // IP pública de la oficina (ficticia)
    private const string Externo = "190.10.20.30";       // un celular cualquiera

    private static async Task<(WebApplication App, TestServer Server)> Levantar(bool habilitado = true, params (string, string?)[] extra)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var config = new Dictionary<string, string?>
        {
            ["Publico:Habilitado"] = habilitado ? "true" : "false",
            ["Publico:ProxiesConfiables:0"] = Conector,
            ["Publico:AdminIpsPermitidas:0"] = Oficina,
            ["Publico:AdminIpsPermitidas:1"] = "192.168.0.0/24",
        };
        foreach (var (k, v) in extra) config[k] = v;
        builder.Configuration.AddInMemoryCollection(config);
        builder.AddPublicacion();
        builder.Services.AddAuthentication().AddCookie("prueba");
        builder.Services.ConfigureApplicationCookie(o => o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest);

        var app = builder.Build();
        app.UsePublicacion();
        app.MapGet("/", () => Results.Content("<html></html>", "text/html"));
        app.MapGet("/doc.pdf", () => Results.Bytes(new byte[] { 1 }, "application/pdf"));
        app.MapGet("/admin/usuarios", () => "panel");
        app.MapGet("/quien", (HttpContext h) => $"{h.Connection.RemoteIpAddress}|{h.Request.Scheme}");
        app.MapPost("/conciliacion-sri/api/comprobantes", () => "recibido");
        await app.StartAsync();
        return (app, app.GetTestServer());
    }

    private static Task<HttpContext> Pedir(TestServer server, string ruta, string ip, Action<HttpRequest>? extra = null) =>
        server.SendAsync(c =>
        {
            c.Request.Path = ruta;
            c.Request.Method = "GET";
            c.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            extra?.Invoke(c.Request);
        });

    private static string Cuerpo(HttpContext c) => new StreamReader(c.Response.Body).ReadToEnd();

    [Fact]
    public async Task Desde_el_conector_se_toma_la_IP_real_del_cliente_y_el_https()
    {
        var (app, server) = await Levantar();
        await using var _ = app;
        var c = await Pedir(server, "/quien", Conector, r => { r.Headers["CF-Connecting-IP"] = Externo; r.Headers["X-Forwarded-Proto"] = "https"; });
        Assert.Equal($"{Externo}|https", Cuerpo(c));
    }

    [Fact]
    public async Task Si_no_viene_del_conector_los_encabezados_se_ignoran()
    {
        var (app, server) = await Levantar();
        await using var _ = app;
        var c = await Pedir(server, "/quien", Externo, r => { r.Headers["CF-Connecting-IP"] = Oficina; r.Headers["X-Forwarded-Proto"] = "https"; });
        Assert.Equal($"{Externo}|http", Cuerpo(c)); // nadie se puede hacer pasar por la oficina mandando el encabezado
    }

    [Fact]
    public async Task Admin_solo_desde_la_oficina_o_la_red_interna()
    {
        var (app, server) = await Levantar();
        await using var _ = app;
        var desdeCelular = await Pedir(server, "/admin/usuarios", Conector, r => r.Headers["CF-Connecting-IP"] = Externo);
        Assert.Equal(StatusCodes.Status403Forbidden, desdeCelular.Response.StatusCode);
        var desdeOficina = await Pedir(server, "/admin/usuarios", Conector, r => r.Headers["CF-Connecting-IP"] = Oficina);
        Assert.Equal(StatusCodes.Status200OK, desdeOficina.Response.StatusCode);
        var interno = await Pedir(server, "/admin/usuarios", "192.168.0.23");
        Assert.Equal(StatusCodes.Status200OK, interno.Response.StatusCode);
        var paginaComun = await Pedir(server, "/", Conector, r => r.Headers["CF-Connecting-IP"] = Externo);
        Assert.Equal(StatusCodes.Status200OK, paginaComun.Response.StatusCode);
    }

    [Fact]
    public async Task Las_paginas_llevan_cabeceras_de_seguridad_y_CSP_y_los_PDF_no_llevan_CSP()
    {
        var (app, server) = await Levantar();
        await using var _ = app;
        var html = await Pedir(server, "/", "192.168.0.23");
        Assert.Equal("nosniff", html.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", html.Response.Headers["X-Frame-Options"].ToString());
        Assert.Contains("frame-ancestors 'none'", html.Response.Headers["Content-Security-Policy"].ToString());
        Assert.Contains("script-src 'self';", html.Response.Headers["Content-Security-Policy"].ToString());
        var pdf = await Pedir(server, "/doc.pdf", "192.168.0.23");
        Assert.Equal("nosniff", pdf.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.False(pdf.Response.Headers.ContainsKey("Content-Security-Policy"));
    }

    [Fact]
    public async Task Habilitado_las_cookies_son_siempre_Secure_aunque_otro_codigo_diga_SameAsRequest()
    {
        var (app, _) = await Levantar(habilitado: true);
        await using var __ = app;
        var monitor = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        Assert.Equal(CookieSecurePolicy.Always, monitor.Get("prueba").Cookie.SecurePolicy);
    }

    [Fact]
    public async Task Deshabilitado_queda_como_hasta_ahora_para_el_HTTP_interno()
    {
        var (app, server) = await Levantar(habilitado: false);
        await using var _ = app;
        var monitor = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        Assert.NotEqual(CookieSecurePolicy.Always, monitor.Get("prueba").Cookie.SecurePolicy);
        var html = await Pedir(server, "/", "192.168.0.23");
        Assert.False(html.Response.Headers.ContainsKey("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Sin_lista_de_admin_no_hay_restriccion_de_red()
    {
        var (app, server) = await Levantar(true, ("Publico:AdminIpsPermitidas:0", null), ("Publico:AdminIpsPermitidas:1", null));
        await using var _ = app;
        var c = await Pedir(server, "/admin/usuarios", Externo);
        Assert.Equal(StatusCodes.Status200OK, c.Response.StatusCode);
    }

    private const string UrlPublica = "https://webapp.paredes.com.ec";

    [Fact]
    public async Task Por_HTTP_directo_se_redirige_a_la_direccion_publica_conservando_ruta_y_consulta()
    {
        var (app, server) = await Levantar(true, ("Publico:UrlPublica", UrlPublica + "/"));
        await using var _ = app;
        var c = await Pedir(server, "/ingresar", "192.168.0.23", r => r.QueryString = new QueryString("?ReturnUrl=%2Fventas"));
        Assert.Equal(StatusCodes.Status302Found, c.Response.StatusCode);
        Assert.Equal("https://webapp.paredes.com.ec/ingresar?ReturnUrl=%2Fventas", c.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Lo_que_llega_por_el_conector_en_HTTPS_no_se_redirige()
    {
        var (app, server) = await Levantar(true, ("Publico:UrlPublica", UrlPublica));
        await using var _ = app;
        var c = await Pedir(server, "/quien", Conector, r => { r.Headers["CF-Connecting-IP"] = Externo; r.Headers["X-Forwarded-Proto"] = "https"; });
        Assert.Equal(StatusCodes.Status200OK, c.Response.StatusCode);
    }

    [Fact]
    public async Task Un_POST_por_HTTP_directo_no_se_redirige_para_no_perder_el_token_de_la_extension()
    {
        var (app, server) = await Levantar(true, ("Publico:UrlPublica", UrlPublica));
        await using var _ = app;
        var c = await server.SendAsync(h =>
        {
            h.Request.Path = "/conciliacion-sri/api/comprobantes";
            h.Request.Method = "POST";
            h.Connection.RemoteIpAddress = IPAddress.Parse("192.168.0.23");
        });
        Assert.Equal(StatusCodes.Status200OK, c.Response.StatusCode);
    }

    [Fact]
    public async Task Sin_publicacion_habilitada_no_hay_redireccion()
    {
        var (app, server) = await Levantar(false, ("Publico:UrlPublica", UrlPublica));
        await using var _ = app;
        var c = await Pedir(server, "/quien", "192.168.0.23");
        Assert.Equal(StatusCodes.Status200OK, c.Response.StatusCode);
    }

    [Theory]
    [InlineData("http://webapp.paredes.com.ec")]
    [InlineData("https://webapp.paredes.com.ec/ingresar")]
    [InlineData("webapp.paredes.com.ec")]
    public async Task Una_UrlPublica_mal_escrita_falla_al_arrancar(string url)
    {
        await Assert.ThrowsAnyAsync<FormatException>(() => Levantar(true, ("Publico:UrlPublica", url)));
    }

    [Theory]
    [InlineData("200.1.2.3", true)]
    [InlineData("::ffff:200.1.2.3", true)]   // IPv4 mapeada en IPv6 (así llega a veces por IIS)
    [InlineData("192.168.0.200", true)]
    [InlineData("192.168.1.5", false)]
    [InlineData("190.10.20.30", false)]
    public void ListaIps_acepta_IPs_sueltas_y_redes(string ip, bool esperado)
    {
        var lista = new ListaIps(new[] { "200.1.2.3", " 192.168.0.0/24 ", "" });
        Assert.Equal(esperado, lista.Contiene(IPAddress.Parse(ip)));
    }

    [Fact]
    public void Una_IP_mal_escrita_en_el_web_config_falla_al_arrancar()
    {
        Assert.ThrowsAny<FormatException>(() => new ListaIps(new[] { "200.1.2" }));
    }
}
