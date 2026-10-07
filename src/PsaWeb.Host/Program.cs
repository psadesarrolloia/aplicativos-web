using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using PsaWeb.Host.Auth;
using PsaWeb.Host.Components;
using PsaWeb.Modules.CierreDeCaja;
using PsaWeb.Modules.CierreDeCaja.Data;
using PsaWeb.Modules.CierreDeCaja.Export;
using PsaWeb.Modules.Kardex;
using PsaWeb.Modules.Ventas;
using PsaWeb.Modules.Reportes;
using PsaWeb.Modules.Reportes.Pwc;
using PsaWeb.Modules.Ats;
using PsaWeb.Datil;
using PsaWeb.Notificaciones;
using PsaWeb.PeachEbills;
using PsaWeb.Modules.ComprobantesElectronicos;
using PsaWeb.Sage50;
using PsaWeb.Seguridad;
using PsaWeb.Identidad;
using Microsoft.EntityFrameworkCore;
using PsaWeb.Conciliacion;
using PsaWeb.Conciliacion.Data;
using PsaWeb.Modules.ConciliacionSri;
using PsaWeb.SageBridge.Cola;

var builder = WebApplication.CreateBuilder(args);

// Publicación por Cloudflare Tunnel (acceso externo, E1): IP real del cliente, cookies Secure, cabeceras, /admin por red, límite general.
var publico = builder.AddPublicacion();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Acceso a datos de Sage 50 (ODBC / Pervasive) + módulo Cierre de Caja.
// Sin cadena de conexión configurada, el módulo usa datos de muestra.
builder.Services.AddSage50(builder.Configuration);
builder.Services.AddCierreDeCaja(builder.Configuration);
builder.Services.AddKardex(builder.Configuration); // Kardex de inventarios (solo lectura, empresa de sesión)
builder.Services.AddVentas(builder.Configuration); // Portal de ventas F1: inventario en tiempo real y precios (solo lectura, empresa de sesión)
builder.Services.AddReportes(builder.Configuration); // Reportes de Access: PWC, Comisiones (Cartera) y Cheques (Bancos)

// Módulo Retenciones (Ola 1). Solo se registra si hay cadena a PeachEBills; sin
// ella las páginas de Comprobantes electrónicos (/fe/*) muestran un aviso de "no configurado" y el resto del
// sitio (piloto Cierre de Caja) sigue funcionando igual.
var peachEbillsConfigurado = !string.IsNullOrWhiteSpace(
    builder.Configuration.GetSection(PeachEbillsOptions.SectionName)["ConnectionString"]);

// Shell F-Shell-0: identidad local (ASP.NET Core Identity) + rate-limiting del
// login. Solo se registra si hay cadena a PsaWebPlataforma.
var plataformaConfigurada = !string.IsNullOrWhiteSpace(
    builder.Configuration.GetSection(PsaWeb.Identidad.ServiceCollectionExtensions.SectionName)["ConnectionString"]);

// De dónde salen empresas y permisos (PLAN-ACCESOS-WEB): Accesos:Fuente = PeachEBills (por defecto) | Web.
var fuenteAccesos = PsaWeb.Seguridad.FuenteAccesos.PeachEBills;
if (peachEbillsConfigurado)
{
    builder.Services.AddPeachEbills(builder.Configuration);
    builder.Services.AddDatil(builder.Configuration);
    builder.Services.AddNotificaciones(builder.Configuration); // SMTP (solicitud de anulación); inerte si no hay Correo:Servidor
    builder.Services.AddComprobantesElectronicos(builder.Configuration); // Ola 1 app #2: facturas / NC / liquidaciones
    builder.Services.AddAts(builder.Configuration); // Ola 1 app #3: ATS (requiere PeachEBills por dicIdentityTypeATS/Establishments/Transmitter)
    // Shell F-Shell-0: directorio de seguridad (empresas + permisos por usuario)
    // y estado de sesión de empresa/ambiente.
    fuenteAccesos = builder.Services.AddSeguridad(builder.Configuration, plataformaConfigurada);

    // Shell F-Shell-3b: los módulos de solo lectura (Cierre de Caja, Kardex)
    // resuelven la empresa/conexión Sage por el RUC de sesión (reemplaza el
    // resolver "sin shell" que registran los módulos).
    builder.Services.AddScoped<
        PsaWeb.Sage50.IResolverEmpresaSage,
        PsaWeb.Host.Cierre.HostResolverEmpresaSage>();
    builder.Services.AddScoped<
        PsaWeb.Modules.Reportes.Comun.IEmpresaSesionInfo,
        PsaWeb.Host.Cierre.HostEmpresaSesionInfo>();
    // AW-5: usuarios de Sage 50 de cada compañía (para elegir y validar el usuario de Sage de cada cuenta en el panel).
    builder.Services.AddMemoryCache();
    builder.Services.AddScoped<PsaWeb.Seguridad.ILectorUsuariosSage, PsaWeb.Host.Auth.LectorUsuariosSageOdbc>();
}

// Interruptor de los módulos que ESCRIBEN en Sage por el Sage Bridge (Ola 2: Compras, Facturas recibidas, Liquidación de importaciones,
// administración del Bridge). Apagado por defecto: producción no los activa hasta el «deploy de escritura»; desarrollo lo enciende en
// appsettings.Development.json. Apagado no se registran, sus rutas dan 404, no salen en el menú y no corren sus migraciones ni el worker.
var escrituraHabilitada = builder.Configuration.GetValue("Escritura:Habilitada", false);
builder.Services.Configure<PsaWeb.Seguridad.EscrituraOptions>(builder.Configuration.GetSection(PsaWeb.Seguridad.EscrituraOptions.SectionName));
if (plataformaConfigurada)
{
    builder.Services.AddIdentidadPlataforma(builder.Configuration);
    // Correos de la cuenta (invitación, recuperación, código por correo, aviso de IP nueva) por el SMTP de Notificaciones.
    builder.Services.AddScoped<IEnviadorCorreoPlataforma, PsaWeb.Host.Auth.EnviadorCorreoNotificaciones>();
    builder.Services.AddMemoryCache();
    builder.Services.AddRateLimiter(opciones =>
    {
        PsaWeb.Identidad.ServiceCollectionExtensions.AgregarPoliticaLimiteLogin(opciones);
        PsaWeb.Host.Auth.PublicacionExtensions.AgregarLimiteGeneral(opciones, publico);
        // Extensión de Chrome (endpoint público por token): 30 subidas por minuto por IP.
        opciones.AddPolicy(Program.PoliticaExtension, contexto => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            contexto.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    });
    // Módulo de Conciliación SRI (F1): staging de comprobantes del SRI, misma
    // base física que PsaWebPlataforma. El endpoint de subida y la pantalla
    // /mi-cuenta/extension solo tienen sentido si hay plataforma (token de API
    // atado a un usuario de Identity).
    builder.Services.AddConciliacion(builder.Configuration);
    // Ola 2: cola del Sage Bridge (la web encola; el servicio PsaSageBridge escribe en Sage). Misma base física.
    builder.Services.AddSageBridgeCola(builder.Configuration);
    // Ola 2: XML de documentos recibidos (almacén §13.1 del plan + descarga por el WS del SRI en segundo plano). Solo con escritura habilitada.
    if (escrituraHabilitada)
    {
        PsaWeb.Recibidos.ServiceCollectionExtensions.AddRecibidos(builder.Services, builder.Configuration);
    }
}

// El módulo de Conciliación SRI (procesador de verificación + worker + página)
// necesita staging (arriba, requiere Plataforma) Y Sage/PeachEbills (para
// resolver la conexión de cada empresa) — solo se activa si ambos están.
var conciliacionSriActiva = plataformaConfigurada && peachEbillsConfigurado;
if (conciliacionSriActiva)
{
    builder.Services.AddConciliacionSri(builder.Configuration);
}

// Ola 2: módulo Compras (lee Sage por ODBC, catálogos de PeachEBills, escribe encolando en el Sage Bridge).
if (escrituraHabilitada && plataformaConfigurada && peachEbillsConfigurado)
{
    PsaWeb.Modules.Compras.ComprasModule.AddCompras(builder.Services);
}

// --- Autenticación -----------------------------------------------------------
var isDevelopment = builder.Environment.IsDevelopment();

if (plataformaConfigurada)
{
    // Shell F-Shell-1: autenticación por cookie de ASP.NET Core Identity (login local).
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = Microsoft.AspNetCore.Identity.IdentityConstants.ExternalScheme;
    }).AddIdentityCookies();

    builder.Services.ConfigureApplicationCookie(options =>
    {
        // La puerta para quien no tiene sesión es la bienvenida; desde ahí se
        // pasa al formulario de login (llevando el returnUrl).
        options.LoginPath = "/bienvenida";
        options.LogoutPath = "/salir";
        options.AccessDeniedPath = "/bienvenida";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "PsaWeb.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
        options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
    });

    // «Confiar en este dispositivo» del segundo paso: 30 días (se invalida al cambiar clave, correo o 2FA: sello de seguridad).
    builder.Services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
        Microsoft.AspNetCore.Identity.IdentityConstants.TwoFactorRememberMeScheme,
        o => o.ExpireTimeSpan = TimeSpan.FromDays(30));
    // La cookie re-valida el sello cada 5 min (cuenta deshabilitada, clave/perfil cambiados) y los circuitos de Blazor igual.
    builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));
    builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider, PsaWeb.Host.Auth.RevalidacionSesion>();
}
else
{
    // Sin plataforma: piloto standalone con Windows Integrated Auth (Negotiate /
    // Kerberos contra AD). En Development, un handler firma como el usuario de
    // Windows local (PREDATOR no está en el dominio).
    const string devScheme = "DevWindows";
    var authentication = builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = isDevelopment ? devScheme : NegotiateDefaults.AuthenticationScheme;
    });
    authentication.AddNegotiate();
    if (isDevelopment)
    {
        authentication.AddScheme<AuthenticationSchemeOptions, DevWindowsAuthHandler>(devScheme, _ => { });
    }
}

builder.Services.AddAuthorization(options =>
{
    // Todo el sitio exige un usuario autenticado. La autorización por rol / grupo AD
    // se define en la ola grande, no en el piloto.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

app.Logger.LogInformation(
    "Cierre de Caja: repositorio {Repo}.",
    CierreDeCajaModule.UsaDatosDeMuestra(app.Configuration) ? "DE MUESTRA" : "ODBC / Sage 50");
app.Logger.LogInformation(
    "Kardex: repositorio {Repo}.",
    PsaWeb.Modules.Kardex.KardexModule.UsaDatosDeMuestra(app.Configuration) ? "DE MUESTRA" : "ODBC / Sage 50");
app.Logger.LogInformation(
    "Reportes (PWC, Comisiones, Cheques): repositorio {Repo}; configuracion {Config}.",
    PsaWeb.Modules.Reportes.ReportesModule.UsaDatosDeMuestra(app.Configuration) ? "DE MUESTRA" : "ODBC / Sage 50",
    PsaWeb.Modules.Reportes.ReportesModule.UsaPlataforma(app.Configuration) ? "en PsaWebPlataforma" : "en memoria");
app.Logger.LogInformation(
    "ATS: módulo {Estado}{Repo}.",
    peachEbillsConfigurado ? "ACTIVO" : "INACTIVO (sin PeachEbills:ConnectionString)",
    peachEbillsConfigurado
        ? $", repositorio {(PsaWeb.Modules.Ats.AtsModule.UsaDatosDeMuestra(app.Configuration) ? "DE MUESTRA" : "ODBC / Sage 50")}"
        : "");
app.Logger.LogInformation(
    "Comprobantes electrónicos (facturas, retenciones, NC, liquidaciones): módulo {Estado}.",
    peachEbillsConfigurado ? "ACTIVO (PeachEBills configurado)" : "INACTIVO (sin PeachEbills:ConnectionString)");
app.Logger.LogInformation(
    "Plataforma (identidad local): {Estado}.",
    plataformaConfigurada ? "ACTIVA" : "INACTIVA (sin Plataforma:ConnectionString)");
app.Logger.LogInformation(
    "Publicación externa: {Estado}; proxies confiables {Proxies}; /admin {Admin}.",
    publico.Habilitado ? "HABILITADA (cookies Secure, HSTS)" : "deshabilitada (HTTP interno)",
    publico.ProxiesConfiables.Count == 0 ? "ninguno" : string.Join(", ", publico.ProxiesConfiables),
    publico.AdminIpsPermitidas.Count == 0 ? "sin restricción de red" : "solo desde " + string.Join(", ", publico.AdminIpsPermitidas));
if (publico.Habilitado && (app.Configuration["AllowedHosts"] ?? "*") == "*")
{
    app.Logger.LogWarning("Publico:Habilitado=true con AllowedHosts=*: define AllowedHosts=webapp.paredes.com.ec;192.168.0.11;localhost en el web.config.");
}
app.Logger.LogInformation(
    "Accesos (empresas y permisos): fuente {Fuente}{Modo}.",
    fuenteAccesos,
    fuenteAccesos == PsaWeb.Seguridad.FuenteAccesos.Web ? " (tabla web, sin GateProvisional)" : " (tablas del .exe, con GateProvisional)");
app.Logger.LogInformation(
    "Conciliación SRI: módulo {Estado}.",
    conciliacionSriActiva ? "ACTIVO" : "INACTIVO (necesita Plataforma:ConnectionString y PeachEbills:ConnectionString)");

// Puesta al día del esquema de PsaWebPlataforma + siembra del primer usuario.
// - En Development: siembra el usuario de prueba (Plataforma:UsuarioDev/ClaveDev).
// - En Producción: aplica migraciones (salvo Plataforma:MigrarAlArrancar=false) y,
//   SOLO si la base no tiene ningún usuario, crea el admin inicial desde
//   Plataforma:AdminInicial:Usuario/Clave. Quitar esas variables tras el 1er arranque.
if (plataformaConfigurada)
{
    await using var scope = app.Services.CreateAsyncScope();
    var seeder = scope.ServiceProvider.GetRequiredService<PsaWeb.Identidad.IdentidadSeeder>();

    var migrarAlArrancar = app.Configuration.GetValue("Plataforma:MigrarAlArrancar", true);
    if (migrarAlArrancar)
    {
        await seeder.MigrarAsync();
        app.Logger.LogInformation("PsaWebPlataforma: migraciones aplicadas.");

        var conciliacionDb = scope.ServiceProvider.GetRequiredService<ConciliacionDbContext>();
        await conciliacionDb.Database.MigrateAsync();
        app.Logger.LogInformation("Conciliación SRI: migraciones aplicadas.");

        var reportesDb = scope.ServiceProvider.GetRequiredService<PsaWeb.Modules.Reportes.Comun.ReportesDbContext>();
        await reportesDb.Database.MigrateAsync();
        app.Logger.LogInformation("Reportes (Cartera / Bancos): migraciones aplicadas.");

        var ventasDb = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<PsaWeb.Modules.Ventas.Prefacturas.VentasDbContext>>()
            .CreateDbContextAsync();
        await ventasDb.Database.MigrateAsync();
        await ventasDb.DisposeAsync();
        app.Logger.LogInformation("Ventas (prefacturas): migraciones aplicadas.");

        if (escrituraHabilitada)
        {
            var bridgeDb = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<PsaWeb.SageBridge.Cola.Data.SageBridgeDbContext>>()
                .CreateDbContextAsync();
            await bridgeDb.Database.MigrateAsync();
            await bridgeDb.DisposeAsync();
            app.Logger.LogInformation("Sage Bridge (cola): migraciones aplicadas.");

            var recibidosDb = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<PsaWeb.Recibidos.Data.RecibidosDbContext>>()
                .CreateDbContextAsync();
            await recibidosDb.Database.MigrateAsync();
            await recibidosDb.DisposeAsync();
            app.Logger.LogInformation("Documentos recibidos (XML): migraciones aplicadas.");
        }
        else
        {
            app.Logger.LogInformation("Módulos de escritura en Sage (Compras, Recibidos, Bridge): DESACTIVADOS (Escritura:Habilitada=false); no se migran sus tablas.");
        }
    }

    if (app.Environment.IsDevelopment())
    {
        var usuarioDev = app.Configuration["Plataforma:UsuarioDev"];
        var claveDev = app.Configuration["Plataforma:ClaveDev"];
        if (!string.IsNullOrWhiteSpace(usuarioDev) && !string.IsNullOrWhiteSpace(claveDev))
        {
            var creado = await seeder.CrearSiNoExisteAsync(
                usuarioDev, claveDev, nombreCompleto: usuarioDev, peachUsername: usuarioDev);
            app.Logger.LogInformation(
                "Usuario de desarrollo {Usuario}: {Estado}.", usuarioDev, creado ? "creado" : "ya existía");
        }
    }
    else
    {
        var adminUsuario = app.Configuration["Plataforma:AdminInicial:Usuario"];
        var adminClave = app.Configuration["Plataforma:AdminInicial:Clave"];
        if (!string.IsNullOrWhiteSpace(adminUsuario) && !string.IsNullOrWhiteSpace(adminClave))
        {
            if (await seeder.HayAlgunUsuarioAsync())
            {
                app.Logger.LogWarning(
                    "Plataforma:AdminInicial está definido pero ya hay usuarios: no se creó nada. " +
                    "Quita esas variables de entorno.");
            }
            else
            {
                await seeder.CrearSiNoExisteAsync(
                    adminUsuario, adminClave, nombreCompleto: adminUsuario, peachUsername: adminUsuario);
                app.Logger.LogWarning(
                    "Admin inicial {Usuario} creado. Verifica que esté en Plataforma:Admins y " +
                    "quita Plataforma:AdminInicial:* del entorno.", adminUsuario);
            }
        }
    }
}

// Primero de todo: la IP y el esquema reales (del conector de Cloudflare), las cabeceras de seguridad y /admin por red.
app.UsePublicacion();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
if (plataformaConfigurada)
{
    PsaWeb.Host.Auth.AccesoModulosExtensions.UseSegundoFactorObligatorio(app);
    app.UseRateLimiter();
    // La extensión sube el reporte del SRI (texto): 10 MB alcanzan de sobra. Se fija antes de que el endpoint lea el cuerpo.
    app.Use(async (http, next) =>
    {
        if (http.Request.Path.StartsWithSegments("/conciliacion-sri/api")
            && http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } tope)
        {
            tope.MaxRequestBodySize = 10 * 1024 * 1024;
        }
        await next();
    });
}
app.UseAntiforgery();

// Los recursos estáticos (CSS, JS del framework, imágenes) no pasan por la
// política de autenticación: si no, un usuario sin sesión no puede ni ver la
// pantalla de login con estilos.
app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(PsaWeb.Host.EnsamblesDeModulos.Todos(escrituraHabilitada));

// Descarga del reporte «Cierre de Caja» en Excel. Re-consulta con las mismas
// fechas para que el archivo coincida siempre con lo que se ve en pantalla.
app.MapGet("/cierre-de-caja/export", async (
        DateOnly desde,
        DateOnly hasta,
        string? ruc,
        System.Security.Claims.ClaimsPrincipal usuario,
        ICierreDeCajaRepository repositorio,
        CierreExcelExporter exportador,
        PsaWeb.Seguridad.ISecurityDirectory? seguridad,
        CancellationToken cancellationToken) =>
    {
        if (desde > hasta)
        {
            return Results.BadRequest("La fecha «Desde» no puede ser mayor que «Hasta».");
        }

        ResultadoCierre resultado;
        if (!string.IsNullOrWhiteSpace(ruc))
        {
            // Con shell: el RUC viene de la página. Sólo se exporta una empresa
            // a la que el usuario tenga acceso.
            var nombre = usuario.Identity?.Name ?? string.Empty;
            var tieneAcceso = seguridad is not null
                && (await seguridad.EmpresasDelUsuarioAsync(nombre, cancellationToken))
                    .Any(e => e.Ruc == ruc);
            if (!tieneAcceso)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            resultado = await repositorio.ObtenerParaRucAsync(ruc, desde, hasta, cancellationToken);
        }
        else
        {
            resultado = await repositorio.ObtenerAsync(desde, hasta, cancellationToken);
        }

        var bytes = exportador.Generar(resultado, desde, hasta);
        return Results.File(bytes, CierreExcelExporter.ContentType, exportador.NombreArchivo(desde, hasta));
    })
    .RequireAuthorization()
    .ExigirModulo("cierre-de-caja");

// Descarga del Kardex en Excel. Re-consulta con el mismo filtro para que el
// archivo coincida con lo que se ve en pantalla.
app.MapGet("/kardex/export", async (
        DateOnly desde,
        DateOnly hasta,
        string? ruc,
        string[]? cuenta,
        string? itemDesde,
        string? itemHasta,
        string[]? items,
        bool? incluirVacios,
        System.Security.Claims.ClaimsPrincipal usuario,
        PsaWeb.Modules.Kardex.Data.IKardexRepository repositorio,
        PsaWeb.Modules.Kardex.Export.KardexExcelExporter exportador,
        PsaWeb.Seguridad.ISecurityDirectory? seguridad,
        CancellationToken cancellationToken) =>
    {
        var filtro = new PsaWeb.Modules.Kardex.Data.FiltroKardex(
            desde, hasta, items ?? Array.Empty<string>(), cuenta ?? Array.Empty<string>(), itemDesde, itemHasta,
            IncluirVacios: incluirVacios ?? true);

        if (!filtro.RangoValido)
        {
            return Results.BadRequest("El rango debe tener al menos un día («Hasta» posterior a «Desde»).");
        }
        if (!filtro.TieneAcotador)
        {
            return Results.BadRequest("Elige al menos un ítem, una cuenta o un rango de ítems.");
        }

        PsaWeb.Modules.Kardex.Data.ResultadoKardex resultado;
        if (!string.IsNullOrWhiteSpace(ruc))
        {
            var nombre = usuario.Identity?.Name ?? string.Empty;
            var tieneAcceso = seguridad is not null
                && (await seguridad.EmpresasDelUsuarioAsync(nombre, cancellationToken))
                    .Any(e => e.Ruc == ruc);
            if (!tieneAcceso)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            resultado = await repositorio.GenerarParaRucAsync(ruc, filtro, cancellationToken);
        }
        else
        {
            resultado = await repositorio.GenerarAsync(filtro, cancellationToken);
        }

        var bytes = exportador.Generar(resultado, desde, hasta);
        return Results.File(
            bytes,
            PsaWeb.Modules.Kardex.Export.KardexExcelExporter.ContentType,
            exportador.NombreArchivo(desde, hasta));
    })
    .RequireAuthorization()
    .ExigirModulo("kardex");

// Ola 2: reporte Excel de una liquidación de importación (port de ApportionImportsCPTDC). Se arma con lo guardado
// (la página exige guardar antes) y las filas de la cuenta en Sage.
// Ruta fuera de /compras/importaciones/{cuenta}: si no, la página la tomaría como una cuenta.
app.MapGet("/exportar/liquidacion-importacion", async (
        string ruc,
        string cuenta,
        System.Security.Claims.ClaimsPrincipal usuario,
        IServiceProvider sp,
        CancellationToken cancellationToken) =>
    {
        var servicio = sp.GetService<PsaWeb.Modules.Compras.Servicios.ServicioLiquidaciones>();
        if (servicio is null) return Results.NotFound();
        var nombre = usuario.Identity?.Name ?? string.Empty;
        if (sp.GetService<PsaWeb.Seguridad.ISecurityDirectory>() is { } seguridad
            && !(await seguridad.EmpresasDelUsuarioAsync(nombre, cancellationToken)).Any(e => e.Ruc == ruc))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        if (!(await servicio.PermisosAsync(nombre, ruc, cancellationToken)).Ver)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        var d = await servicio.AbrirAsync(ruc, cuenta, cancellationToken);
        if (d is null || d.LiquidacionId is null) return Results.NotFound("No hay una liquidación guardada para esa cuenta.");
        if (d.MotivoSinReporte(d.Items, d.Gastos) is { } motivo) return Results.BadRequest(motivo);
        var gastos = d.GastosSinLiquidacion(d.Gastos);
        var bytes = PsaWeb.Modules.Compras.Importaciones.ReporteLiquidacion.Generar(
            await servicio.NombreEmpresaAsync(ruc, cancellationToken), d.Cuenta, gastos, d.Items);
        return Results.File(bytes, PsaWeb.Modules.Compras.Importaciones.ReporteLiquidacion.ContentType,
            PsaWeb.Modules.Compras.Importaciones.ReporteLiquidacion.NombreArchivo(d.Cuenta.Cuenta));
    })
    .RequireAuthorization()
    .ExigirModulo("compras-importaciones");

// Descarga del reporte PWC (cuentas por cobrar) en Excel. Re-consulta con el mismo filtro y usa la
// personalización guardada de la empresa (encabezado, cobrador, columnas).
app.MapGet("/cartera/pwc/export", async (
        string? ruc,
        DateOnly? emisionDesde,
        DateOnly? emisionHasta,
        DateOnly? venceDesde,
        DateOnly? venceHasta,
        string? cliente,
        string? factura,
        string? anunciante,
        string[]? ciudad,
        System.Security.Claims.ClaimsPrincipal usuario,
        PsaWeb.Modules.Reportes.Pwc.IPwcRepository repositorio,
        PsaWeb.Modules.Reportes.Pwc.PwcExcelExporter exportador,
        PsaWeb.Modules.Reportes.Comun.IServicioConfiguracionReportes configuracion,
        PsaWeb.Seguridad.ISecurityDirectory? seguridad,
        CancellationToken cancellationToken) =>
    {
        var filtro = new PsaWeb.Modules.Reportes.Pwc.FiltroPwc(
            emisionDesde, emisionHasta, venceDesde, venceHasta, cliente, factura, anunciante,
            ciudad is { Length: > 0 } ? ciudad : null);
        if (!filtro.RangoEmisionValido || !filtro.RangoVenceValido)
        {
            return Results.BadRequest("En los rangos de fechas, «Hasta» no puede ser anterior a «Desde».");
        }

        var nombreSesion = "";
        PsaWeb.Modules.Reportes.Pwc.ResultadoPwc resultado;
        if (!string.IsNullOrWhiteSpace(ruc))
        {
            var nombre = usuario.Identity?.Name ?? string.Empty;
            var empresa = seguridad is null
                ? null
                : (await seguridad.EmpresasDelUsuarioAsync(nombre, cancellationToken)).FirstOrDefault(e => e.Ruc == ruc);
            if (empresa is null)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            nombreSesion = empresa.Nombre;
            resultado = await repositorio.GenerarParaRucAsync(ruc, filtro, cancellationToken);
        }
        else
        {
            resultado = await repositorio.GenerarAsync(filtro, cancellationToken);
        }

        var claveConfig = string.IsNullOrWhiteSpace(ruc) ? "_sin-empresa" : ruc;
        var cfg = await configuracion.ObtenerAsync<PsaWeb.Modules.Reportes.Comun.ConfiguracionPwc>(
            claveConfig, PsaWeb.Modules.Reportes.Comun.ClavesReporte.Pwc, cancellationToken);
        var emp = await configuracion.ObtenerAsync<PsaWeb.Modules.Reportes.Comun.ConfiguracionEmpresa>(
            claveConfig, PsaWeb.Modules.Reportes.Comun.ClavesReporte.Empresa, cancellationToken);
        var nombreEmpresa = string.IsNullOrWhiteSpace(emp.NombreEmpresa) ? nombreSesion : emp.NombreEmpresa.Trim();
        var corte = DateOnly.FromDateTime(DateTime.Today);

        var bytes = exportador.Generar(resultado, cfg, nombreEmpresa, corte, filtro.Describir());
        return Results.File(
            bytes,
            PsaWeb.Modules.Reportes.Pwc.PwcExcelExporter.ContentType,
            exportador.NombreArchivo(cfg, nombreEmpresa, corte));
    })
    .RequireAuthorization()
    .ExigirModulo("reporte-pwc");

// Descarga del reporte de Comisiones en Excel. Re-consulta con el mismo filtro (incluidas las opciones
// C1/C2 del reporte heredado) y usa la personalización guardada de la empresa.
app.MapGet("/cartera/comisiones/export", async (
        string? ruc,
        string? reciboDesde,
        string? reciboHasta,
        DateOnly? fechaDesde,
        DateOnly? fechaHasta,
        string? cliente,
        string? ciudad,
        bool? rangoNumerico,
        bool? abonoPorRecibo,
        System.Security.Claims.ClaimsPrincipal usuario,
        PsaWeb.Modules.Reportes.Comisiones.IComisionesRepository repositorio,
        PsaWeb.Modules.Reportes.Comisiones.ComisionesExcelExporter exportador,
        PsaWeb.Modules.Reportes.Comun.IServicioConfiguracionReportes configuracion,
        PsaWeb.Seguridad.ISecurityDirectory? seguridad,
        CancellationToken cancellationToken) =>
    {
        var filtro = new PsaWeb.Modules.Reportes.Comisiones.FiltroComisiones(
            reciboDesde, reciboHasta, fechaDesde, fechaHasta, cliente, ciudad,
            rangoNumerico ?? false, abonoPorRecibo ?? false);
        if (!filtro.RangoRecibosValido)
        {
            return Results.BadRequest("El rango de recibos necesita «desde» y «hasta», solo con números.");
        }
        if (!filtro.RangoFechasValido)
        {
            return Results.BadRequest("En las fechas del recibo, «Hasta» no puede ser anterior a «Desde».");
        }
        if (!filtro.TieneAcotador)
        {
            return Results.BadRequest("Indica un rango de recibos o un rango de fechas del recibo.");
        }

        var nombreEmpresa = "";
        PsaWeb.Modules.Reportes.Comisiones.ResultadoComisiones resultado;
        if (!string.IsNullOrWhiteSpace(ruc))
        {
            var nombre = usuario.Identity?.Name ?? string.Empty;
            var empresa = seguridad is null
                ? null
                : (await seguridad.EmpresasDelUsuarioAsync(nombre, cancellationToken)).FirstOrDefault(e => e.Ruc == ruc);
            if (empresa is null)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            nombreEmpresa = empresa.Nombre;
            resultado = await repositorio.GenerarParaRucAsync(ruc, filtro, cancellationToken);
        }
        else
        {
            resultado = await repositorio.GenerarAsync(filtro, cancellationToken);
        }

        var claveConfig = string.IsNullOrWhiteSpace(ruc) ? "_sin-empresa" : ruc;
        var cfg = await configuracion.ObtenerAsync<PsaWeb.Modules.Reportes.Comun.ConfiguracionComisiones>(
            claveConfig, PsaWeb.Modules.Reportes.Comun.ClavesReporte.Comisiones, cancellationToken);
        var emp = await configuracion.ObtenerAsync<PsaWeb.Modules.Reportes.Comun.ConfiguracionEmpresa>(
            claveConfig, PsaWeb.Modules.Reportes.Comun.ClavesReporte.Empresa, cancellationToken);
        if (!string.IsNullOrWhiteSpace(emp.NombreEmpresa))
        {
            nombreEmpresa = emp.NombreEmpresa.Trim();
        }

        var bytes = exportador.Generar(resultado, cfg, filtro);
        return Results.File(
            bytes,
            PsaWeb.Modules.Reportes.Comisiones.ComisionesExcelExporter.ContentType,
            exportador.NombreArchivo(nombreEmpresa, filtro, DateOnly.FromDateTime(DateTime.Today)));
    })
    .RequireAuthorization()
    .ExigirModulo("reporte-comisiones");

// Cheques y comprobantes de egreso: PDF A4 con medidas en mm (imprimir al 100 %), vista previa PNG y hoja
// de prueba de calibración. Sólo se imprimen pagos reales (diario 2 / tipo 5) de una empresa a la que el
// usuario tiene acceso; el repositorio vuelve a exigirlo aunque se manipule la URL.
static async Task<(bool Ok, string Nombre)> AccesoEmpresaAsync(
    string? ruc,
    System.Security.Claims.ClaimsPrincipal usuario,
    PsaWeb.Seguridad.ISecurityDirectory? seguridad,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(ruc))
    {
        return (true, "");
    }
    var nombre = usuario.Identity?.Name ?? string.Empty;
    var empresa = seguridad is null
        ? null
        : (await seguridad.EmpresasDelUsuarioAsync(nombre, cancellationToken)).FirstOrDefault(e => e.Ruc == ruc);
    return empresa is null ? (false, "") : (true, empresa.Nombre);
}

// Portal de ventas: PDF de la cotización/prefactura (descarga en el dispositivo del vendedor). Exige acceso a la empresa de la prefactura.
app.MapGet("/ventas/prefacturas/{id:int}/pdf", async (
        int id,
        string? ruc,
        System.Security.Claims.ClaimsPrincipal usuario,
        PsaWeb.Modules.Ventas.Prefacturas.ServicioPrefacturas servicio,
        PsaWeb.Modules.Ventas.Prefacturas.ServicioPermisosVentas permisos,
        PsaWeb.Seguridad.ISecurityDirectory? seguridad,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(ruc))
        {
            return Results.BadRequest("Falta la empresa.");
        }
        var (ok, _) = await AccesoEmpresaAsync(ruc, usuario, seguridad, cancellationToken);
        if (!ok)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        // Permiso de ver prefacturas (quSalesQte); sin «cerrar» solo las propias (las ajenas responden 404, como si no existieran).
        var actor = await permisos.ActorAsync(usuario.Identity?.Name ?? string.Empty, ruc, cancellationToken);
        if (!actor.Permisos.VerPrefacturas)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        var prefactura = await servicio.ObtenerAsync(ruc, id, actor, cancellationToken);
        if (prefactura is null)
        {
            return Results.NotFound("No existe esa prefactura.");
        }
        return Results.File(
            PsaWeb.Ventas.Prefacturas.PrefacturaPdf.Generar(prefactura),
            "application/pdf",
            PsaWeb.Ventas.Prefacturas.PrefacturaPdf.NombreDeArchivo(prefactura));
    })
    .RequireAuthorization()
    .ExigirModulo("ventas-prefacturas");

app.MapGet("/bancos/cheques/pdf", async (
        string? ruc,
        long[]? po,
        bool? cheque,
        bool? comprobante,
        System.Security.Claims.ClaimsPrincipal usuario,
        PsaWeb.Modules.Reportes.Cheques.ServicioImpresionCheques servicio,
        PsaWeb.Seguridad.ISecurityDirectory? seguridad,
        CancellationToken cancellationToken) =>
    {
        var opciones = new PsaWeb.Modules.Reportes.Cheques.OpcionesImpresion(cheque ?? true, comprobante ?? true);
        if (po is not { Length: > 0 })
        {
            return Results.BadRequest("Elige al menos un pago.");
        }
        if (!opciones.Valida)
        {
            return Results.BadRequest("Elige al menos «cheque» o «comprobante de egreso».");
        }
        if (po.Length > PsaWeb.Modules.Reportes.Cheques.ServicioImpresionCheques.MaximoPagos)
        {
            return Results.BadRequest($"Se pueden imprimir hasta {PsaWeb.Modules.Reportes.Cheques.ServicioImpresionCheques.MaximoPagos} pagos por vez.");
        }

        var (ok, nombre) = await AccesoEmpresaAsync(ruc, usuario, seguridad, cancellationToken);
        if (!ok)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var pdf = await servicio.GenerarPdfAsync(ruc, nombre, po, opciones, cancellationToken);
        return pdf is null
            ? Results.NotFound("No se encontraron esos pagos.")
            : Results.File(pdf, PsaWeb.Modules.Reportes.Cheques.ChequePdfRenderer.ContentType); // inline: se abre en el visor
    })
    .RequireAuthorization()
    .ExigirModulo("cheques");

app.MapGet("/bancos/cheques/vista", async (
        string? ruc,
        long[]? po,
        bool? cheque,
        bool? comprobante,
        System.Security.Claims.ClaimsPrincipal usuario,
        PsaWeb.Modules.Reportes.Cheques.ServicioImpresionCheques servicio,
        PsaWeb.Seguridad.ISecurityDirectory? seguridad,
        CancellationToken cancellationToken) =>
    {
        var opciones = new PsaWeb.Modules.Reportes.Cheques.OpcionesImpresion(cheque ?? true, comprobante ?? true);
        if (po is not { Length: > 0 } || !opciones.Valida)
        {
            return Results.BadRequest("Elige un pago y qué imprimir.");
        }

        var (ok, nombre) = await AccesoEmpresaAsync(ruc, usuario, seguridad, cancellationToken);
        if (!ok)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var png = await servicio.GenerarVistaPreviaAsync(ruc, nombre, po, opciones, cancellationToken);
        return png is null ? Results.NotFound("No se encontró ese pago.") : Results.File(png, "image/png");
    })
    .RequireAuthorization()
    .ExigirModulo("cheques");

app.MapGet("/bancos/cheques/prueba", async (
        string? ruc,
        System.Security.Claims.ClaimsPrincipal usuario,
        PsaWeb.Modules.Reportes.Cheques.ServicioImpresionCheques servicio,
        PsaWeb.Seguridad.ISecurityDirectory? seguridad,
        CancellationToken cancellationToken) =>
    {
        var (ok, nombre) = await AccesoEmpresaAsync(ruc, usuario, seguridad, cancellationToken);
        if (!ok)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var pdf = await servicio.GenerarHojaPruebaAsync(ruc, nombre, cancellationToken);
        return Results.File(pdf, PsaWeb.Modules.Reportes.Cheques.ChequePdfRenderer.ContentType);
    })
    .RequireAuthorization()
    .ExigirModulo("cheques");

// Descarga del ATS en XML. Re-arma el `ivaType` con el mismo período para que
// el archivo coincida con lo que se ve en pantalla; es el archivo que luego se
// sube al DIMM real.
app.MapGet("/ats/export", async (
        int anio,
        int mes,
        string? ruc,
        System.Security.Claims.ClaimsPrincipal usuario,
        PsaWeb.Modules.Ats.Data.IAtsRepository repositorio,
        PsaWeb.Seguridad.ISecurityDirectory? seguridad,
        CancellationToken cancellationToken) =>
    {
        var filtro = new PsaWeb.Modules.Ats.Data.FiltroAts(anio, mes);
        if (!filtro.Valido)
        {
            return Results.BadRequest("El año no puede ser mayor al actual, y el mes debe estar entre 01 y 12.");
        }

        PsaWeb.Ats.Esquema.ivaType ats;
        if (!string.IsNullOrWhiteSpace(ruc))
        {
            var nombre = usuario.Identity?.Name ?? string.Empty;
            var tieneAcceso = seguridad is not null
                && (await seguridad.EmpresasDelUsuarioAsync(nombre, cancellationToken))
                    .Any(e => e.Ruc == ruc);
            if (!tieneAcceso)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            ats = await repositorio.GenerarParaRucAsync(ruc, filtro, cancellationToken);
        }
        else
        {
            ats = await repositorio.GenerarAsync(filtro, cancellationToken);
        }

        var xml = PsaWeb.Ats.EscritorXmlAts.Serializar(ats);
        return Results.File(xml, "application/xml", $"ATS_{ats.IdInformante}_{anio:0000}{mes:00}.xml");
    })
    .RequireAuthorization()
    .ExigirModulo("ats");

// Descarga del Talón Resumen en PDF (F5.6). Re-arma el `ivaType` con el mismo
// período; "Fecha de Generación" es la del momento de la descarga, igual que
// el DIMM real.
app.MapGet("/ats/talon-resumen", async (
        int anio,
        int mes,
        string? ruc,
        System.Security.Claims.ClaimsPrincipal usuario,
        PsaWeb.Modules.Ats.Data.IAtsRepository repositorio,
        PsaWeb.Seguridad.ISecurityDirectory? seguridad,
        CancellationToken cancellationToken) =>
    {
        var filtro = new PsaWeb.Modules.Ats.Data.FiltroAts(anio, mes);
        if (!filtro.Valido)
        {
            return Results.BadRequest("El año no puede ser mayor al actual, y el mes debe estar entre 01 y 12.");
        }

        PsaWeb.Ats.Esquema.ivaType ats;
        if (!string.IsNullOrWhiteSpace(ruc))
        {
            var nombre = usuario.Identity?.Name ?? string.Empty;
            var tieneAcceso = seguridad is not null
                && (await seguridad.EmpresasDelUsuarioAsync(nombre, cancellationToken))
                    .Any(e => e.Ruc == ruc);
            if (!tieneAcceso)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            ats = await repositorio.GenerarParaRucAsync(ruc, filtro, cancellationToken);
        }
        else
        {
            ats = await repositorio.GenerarAsync(filtro, cancellationToken);
        }

        var info = PsaWeb.Ats.TalonResumen.ArmadorTalonResumenAts.Armar(ats, DateTime.Now);
        var pdf = PsaWeb.Ats.TalonResumen.TalonResumenPdfBuilder.Generar(info);
        return Results.File(pdf, "application/pdf", $"TRSMN-ATS-{mes:00}-{anio:0000}-{ats.IdInformante}.pdf");
    })
    .RequireAuthorization()
    .ExigirModulo("ats");

// Subida del reporte de "Comprobantes electrónicos recibidos" del SRI, desde
// la extensión de Chrome del módulo de Conciliación SRI (F1). No usa la
// cookie de Identity — se autentica por token de API (Bearer) vía
// TokenExtensionEndpointFilter, por eso AllowAnonymous(): la policy de cookie
// del sitio no aplica acá, la autenticación la hace el filtro.
app.MapPost("/conciliacion-sri/api/comprobantes", async (
        SubidaReporteRequest body,
        HttpContext http,
        UserManager<UsuarioApp> usuarios,
        PsaWeb.Seguridad.ISecurityDirectory? seguridad,
        IRepositorioComprobantesSri repositorio,
        CancellationToken cancellationToken) =>
    {
        var usuarioId = (string)http.Items[TokenExtensionEndpointFilter.ItemUsuarioId]!;
        var usuario = await usuarios.FindByIdAsync(usuarioId);
        if (usuario is null)
        {
            return Results.Unauthorized();
        }

        if (!usuario.Activo)
        {
            return Results.Unauthorized();
        }
        if (seguridad is not null)
        {
            // Misma regla que la página: empresa asignada y llave de Conciliación SRI (quconcsri) en esa empresa.
            var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, usuario.UserName ?? string.Empty) }, "token-extension"));
            var veredicto = await PsaWeb.Host.Auth.FiltroAccesoModulo.EvaluarAsync(principal, body.Ruc, "conciliacion-sri", seguridad, cancellationToken);
            if (veredicto != PsaWeb.Host.Auth.VeredictoAcceso.Permitido)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
        }

        try
        {
            var resultado = await repositorio.GuardarReporteAsync(body.Ruc, body.ContenidoReporte, usuarioId, cancellationToken);
            // Ola 2 (§4.4): se baja enseguida, detrás, el XML de las claves del reporte que todavía no lo tienen (el WS lo entrega
            // solo ~15 días). Facturas, notas de crédito y retenciones.
            if (http.RequestServices.GetService<PsaWeb.Recibidos.ColaDescargaXml>() is { } colaXml)
            {
                var claves = PsaWeb.Conciliacion.Data.LectorReporteComprobantesSri.Parsear(body.ContenidoReporte, body.Ruc).Filas
                    .Select(f => f.ClaveAcceso)
                    .Where(c => PsaWeb.Recibidos.ServicioDescargaXml.CodigoTipo(c) is "01" or "04" or "07")
                    .ToList();
                colaXml.Encolar(new PsaWeb.Recibidos.PedidoDescargaXml(body.Ruc, claves, usuario.UserName ?? usuarioId));
            }
            return Results.Ok(resultado);
        }
        catch (FormatoReporteInvalidoException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    })
    .AllowAnonymous()
    .RequireRateLimiting(Program.PoliticaExtension)
    .AddEndpointFilter<TokenExtensionEndpointFilter>();

// Re-firma la cookie con los claims al día (perfil, 2FA) — p. ej. después de activar la verificación en dos pasos, que se hace desde
// el circuito interactivo y no puede tocar la cookie. Solo redirige a rutas locales.
if (plataformaConfigurada)
{
    app.MapGet("/mi-cuenta/refrescar-sesion", async (
            string? volver,
            System.Security.Claims.ClaimsPrincipal principal,
            SignInManager<UsuarioApp> signIn,
            UserManager<UsuarioApp> usuarios) =>
        {
            if (await usuarios.GetUserAsync(principal) is { Activo: true } u)
            {
                await signIn.RefreshSignInAsync(u);
            }
            var destino = !string.IsNullOrEmpty(volver) && volver.StartsWith('/') && !volver.StartsWith("//") ? volver : "/";
            return Results.LocalRedirect(destino);
        })
        .RequireAuthorization();
}

app.Run();

public partial class Program
{
    /// <summary>Política de rate-limiting del endpoint de la extensión de Chrome.</summary>
    internal const string PoliticaExtension = "extension";
}
