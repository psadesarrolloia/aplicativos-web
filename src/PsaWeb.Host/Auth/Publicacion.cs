using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;

namespace PsaWeb.Host.Auth;

/// <summary>
/// Sección <c>Publico</c> (E1 del acceso externo, PLAN-ACCESOS-WEB / acceso externo): cómo se comporta el sitio publicado detrás de Cloudflare
/// Tunnel. Todo es configurable en el web.config (<c>Publico__Habilitado</c>, <c>Publico__ProxiesConfiables__0</c>, …) y con
/// <see cref="Habilitado"/> en false el sitio funciona como hasta ahora (HTTP interno).
/// </summary>
public sealed class PublicacionOptions
{
    public const string SectionName = "Publico";

    /// <summary>
    /// true = el sitio se usa por HTTPS público (webapp.paredes.com.ec): cookies siempre «Secure». Con false, por HTTP interno las cookies
    /// no se podrían guardar, por eso se enciende recién con el túnel funcionando.
    /// </summary>
    public bool Habilitado { get; set; }

    /// <summary>
    /// IPs (o redes CIDR) del conector cloudflared: solo de ellas se aceptan <see cref="EncabezadoIpCliente"/> y X-Forwarded-Proto. Vacía =
    /// no se confía en ningún encabezado (la IP es la de la conexión).
    /// </summary>
    public List<string> ProxiesConfiables { get; set; } = new();

    /// <summary>Encabezado con la IP real del cliente que pone Cloudflare.</summary>
    public string EncabezadoIpCliente { get; set; } = "CF-Connecting-IP";

    /// <summary>
    /// IPs o redes CIDR desde las que se puede usar /admin (p. ej. la IP pública de la oficina y 192.168.0.0/24). Vacía = sin restricción
    /// por red (como hasta ahora). La IP es la real del cliente (después de los encabezados del proxy).
    /// </summary>
    public List<string> AdminIpsPermitidas { get; set; } = new();

    /// <summary>Peticiones por minuto por IP en todo el sitio (0 = sin límite). Holgado: en la oficina todos salen por la misma IP pública.</summary>
    public int PeticionesPorMinutoPorIp { get; set; } = 1200;

    /// <summary>
    /// Dirección pública (p. ej. https://webapp.paredes.com.ec). Con <see cref="Habilitado"/>, quien abra el sitio por HTTP directo
    /// (http://192.168.0.11:8088, marcador viejo) es redirigido acá en lugar de ver el error del login sin HTTPS. Solo GET/HEAD: un POST
    /// redirigido a otro origen pierde el encabezado Authorization (la extensión con la dirección vieja daría «token no válido»).
    /// </summary>
    public string? UrlPublica { get; set; }
}

/// <summary>Lista de IPs y redes CIDR (IPv4 / IPv6; una IPv4 mapeada en IPv6 se compara como IPv4).</summary>
public sealed class ListaIps
{
    private readonly List<System.Net.IPNetwork> _redes = new();

    public ListaIps(IEnumerable<string>? entradas)
    {
        foreach (var e in entradas ?? Enumerable.Empty<string>())
        {
            var t = e?.Trim();
            if (string.IsNullOrEmpty(t)) continue;
            // IPAddress.Parse acepta formas viejas de IPv4 («200.1.2» = 200.1.0.2): una IP mal tipeada habilitaría otra dirección en silencio.
            var direccion = t.Split('/')[0];
            if (!direccion.Contains(':') && !System.Text.RegularExpressions.Regex.IsMatch(direccion, @"^\d{1,3}(\.\d{1,3}){3}$"))
            {
                throw new FormatException($"IP inválida en la configuración: «{t}» (se esperan 4 números, p. ej. 200.1.2.3 o 192.168.0.0/24).");
            }
            if (t.Contains('/'))
            {
                _redes.Add(System.Net.IPNetwork.Parse(t));
            }
            else
            {
                var ip = Normalizar(IPAddress.Parse(t));
                _redes.Add(new System.Net.IPNetwork(ip, ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128));
            }
        }
    }

    public bool Vacia => _redes.Count == 0;

    public IReadOnlyList<System.Net.IPNetwork> Redes => _redes;

    public bool Contiene(IPAddress? ip) => ip is not null && _redes.Any(r => r.Contains(Normalizar(ip)));

    public static IPAddress Normalizar(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
}

public static class PublicacionExtensions
{
    public const string PoliticaGeneral = "general";

    /// <summary>Servicios: opciones, encabezados del proxy, cookies Secure (si está habilitado) y el límite general por IP.</summary>
    public static PublicacionOptions AddPublicacion(this WebApplicationBuilder builder)
    {
        var opciones = builder.Configuration.GetSection(PublicacionOptions.SectionName).Get<PublicacionOptions>() ?? new PublicacionOptions();
        // Valida las listas al arrancar (una IP mal escrita en el web.config tiene que fallar enseguida, no en la primera petición).
        var proxies = new ListaIps(opciones.ProxiesConfiables);
        _ = new ListaIps(opciones.AdminIpsPermitidas);
        if (!string.IsNullOrWhiteSpace(opciones.UrlPublica)
            && !(Uri.TryCreate(opciones.UrlPublica, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps && url.AbsolutePath == "/"))
        {
            throw new FormatException($"Publico:UrlPublica inválida: «{opciones.UrlPublica}» (se espera https://dominio, sin ruta).");
        }
        builder.Services.AddSingleton(opciones);

        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardedForHeaderName = opciones.EncabezadoIpCliente;
            o.ForwardLimit = 1;
            o.KnownProxies.Clear();
            o.KnownNetworks.Clear(); // por defecto confía en loopback: solo el conector configurado
            foreach (var red in proxies.Redes) o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(red.BaseAddress, red.PrefixLength));
        });

        if (opciones.Habilitado)
        {
            // PostConfigure: gana sobre el SameAsRequest de ConfigureApplicationCookie (que se registra después).
            builder.Services.PostConfigureAll<CookieAuthenticationOptions>(o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always);
            builder.Services.PostConfigure<AntiforgeryOptions>(o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always);
            builder.Services.AddHsts(o =>
            {
                o.MaxAge = TimeSpan.FromDays(180);
                o.IncludeSubDomains = false; // otros subdominios de paredes.com.ec no son de esta app
            });
        }
        return opciones;
    }

    /// <summary>Política del límite general (la registra el AddRateLimiter del Host).</summary>
    public static void AgregarLimiteGeneral(Microsoft.AspNetCore.RateLimiting.RateLimiterOptions limiter, PublicacionOptions opciones)
    {
        if (opciones.PeticionesPorMinutoPorIp <= 0) return;
        limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = opciones.PeticionesPorMinutoPorIp, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    }

    /// <summary>
    /// Middleware: IP real del cliente (antes que todo), cabeceras de seguridad y /admin solo desde las redes permitidas.
    /// Va al principio del pipeline.
    /// </summary>
    public static void UsePublicacion(this WebApplication app)
    {
        app.UseForwardedHeaders();
        var opciones = app.Services.GetRequiredService<PublicacionOptions>();
        var admin = new ListaIps(opciones.AdminIpsPermitidas);
        var urlPublica = opciones.Habilitado && !string.IsNullOrWhiteSpace(opciones.UrlPublica) ? opciones.UrlPublica.TrimEnd('/') : null;

        app.Use(async (http, next) =>
        {
            AplicarCabeceras(http);
            // Por HTTP directo el login ya no funciona (cookies Secure): se manda a la dirección pública. Lo que llega por el conector ya es
            // HTTPS (X-Forwarded-Proto), así que no hay bucle.
            if (urlPublica is not null && !http.Request.IsHttps
                && (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)))
            {
                http.Response.Redirect(urlPublica + http.Request.PathBase + http.Request.Path + http.Request.QueryString);
                return;
            }
            if (!admin.Vacia && EsRutaAdmin(http.Request.Path) && !admin.Contiene(http.Connection.RemoteIpAddress))
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                await http.Response.WriteAsync("La administración solo se puede usar desde la red de la oficina.");
                return;
            }
            await next();
        });
    }

    public static bool EsRutaAdmin(PathString path) => path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase);

    /// <summary>Política de contenido de las páginas: sin scripts de terceros ni en línea, no se puede incrustar en otro sitio.</summary>
    public const string PoliticaContenido =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    private static void AplicarCabeceras(HttpContext http)
    {
        http.Response.OnStarting(() =>
        {
            var h = http.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            h["X-Frame-Options"] = "DENY";
            h.Remove("X-Powered-By");
            // Solo a las páginas: un PDF o un Excel no la necesitan y al visor de PDF del navegador le puede molestar.
            if (http.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
            {
                h["Content-Security-Policy"] = PoliticaContenido;
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>¿Puede esta IP usar el panel? (la guardia de páginas lo usa dentro del circuito de Blazor, donde no pasa el middleware).</summary>
    public static bool AdminPermitido(PublicacionOptions opciones, IPAddress? ip)
    {
        var lista = new ListaIps(opciones.AdminIpsPermitidas);
        return lista.Vacia || lista.Contiene(ip);
    }
}
