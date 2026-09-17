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
using PsaWeb.Modules.Ats;
using PsaWeb.Datil;
using PsaWeb.Notificaciones;
using PsaWeb.PeachEbills;
using PsaWeb.Modules.Retenciones;
using PsaWeb.Modules.FacturacionElectronica;
using PsaWeb.Sage50;
using PsaWeb.Seguridad;
using PsaWeb.Identidad;
using Microsoft.EntityFrameworkCore;
using PsaWeb.Conciliacion;
using PsaWeb.Conciliacion.Data;
using PsaWeb.Modules.ConciliacionSri;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Acceso a datos de Sage 50 (ODBC / Pervasive) + módulo Cierre de Caja.
// Sin cadena de conexión configurada, el módulo usa datos de muestra.
builder.Services.AddSage50(builder.Configuration);
builder.Services.AddCierreDeCaja(builder.Configuration);
builder.Services.AddKardex(builder.Configuration); // Kardex de inventarios (solo lectura, empresa de sesión)

// Módulo Retenciones (Ola 1). Solo se registra si hay cadena a PeachEBills; sin
// ella la página /retenciones muestra un aviso de "no configurado" y el resto del
// sitio (piloto Cierre de Caja) sigue funcionando igual.
var peachEbillsConfigurado = !string.IsNullOrWhiteSpace(
    builder.Configuration.GetSection(PeachEbillsOptions.SectionName)["ConnectionString"]);
if (peachEbillsConfigurado)
{
    builder.Services.AddPeachEbills(builder.Configuration);
    builder.Services.AddDatil(builder.Configuration);
    builder.Services.AddNotificaciones(builder.Configuration); // SMTP (solicitud de anulación); inerte si no hay Correo:Servidor
    builder.Services.AddRetenciones(builder.Configuration);
    builder.Services.AddFacturacionElectronica(builder.Configuration); // Ola 1 app #2: facturas / NC / liquidaciones
    builder.Services.AddAts(builder.Configuration); // Ola 1 app #3: ATS (requiere PeachEBills por dicIdentityTypeATS/Establishments/Transmitter)
    // Shell F-Shell-0: directorio de seguridad (empresas + permisos por usuario)
    // y estado de sesión de empresa/ambiente.
    builder.Services.AddSeguridad();

    // Shell F-Shell-3b: los módulos de solo lectura (Cierre de Caja, Kardex)
    // resuelven la empresa/conexión Sage por el RUC de sesión (reemplaza el
    // resolver "sin shell" que registran los módulos).
    builder.Services.AddScoped<
        PsaWeb.Sage50.IResolverEmpresaSage,
        PsaWeb.Host.Cierre.HostResolverEmpresaSage>();
}

// Shell F-Shell-0: identidad local (ASP.NET Core Identity) + rate-limiting del
// login. Solo se registra si hay cadena a PsaWebPlataforma. Todavía NO fija el
// esquema de auth por defecto — eso lo hace F-Shell-1 con las pantallas de login.
var plataformaConfigurada = !string.IsNullOrWhiteSpace(
    builder.Configuration.GetSection(PsaWeb.Identidad.ServiceCollectionExtensions.SectionName)["ConnectionString"]);
if (plataformaConfigurada)
{
    builder.Services.AddIdentidadPlataforma(builder.Configuration);
    builder.Services.AddRateLimiter(PsaWeb.Identidad.ServiceCollectionExtensions.AgregarPoliticaLimiteLogin);
    // Módulo de Conciliación SRI (F1): staging de comprobantes del SRI, misma
    // base física que PsaWebPlataforma. El endpoint de subida y la pantalla
    // /mi-cuenta/extension solo tienen sentido si hay plataforma (token de API
    // atado a un usuario de Identity).
    builder.Services.AddConciliacion(builder.Configuration);
}

// El módulo de Conciliación SRI (procesador de verificación + worker + página)
// necesita staging (arriba, requiere Plataforma) Y Sage/PeachEbills (para
// resolver la conexión de cada empresa) — solo se activa si ambos están.
var conciliacionSriActiva = plataformaConfigurada && peachEbillsConfigurado;
if (conciliacionSriActiva)
{
    builder.Services.AddConciliacionSri(builder.Configuration);
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
    "ATS: módulo {Estado}{Repo}.",
    peachEbillsConfigurado ? "ACTIVO" : "INACTIVO (sin PeachEbills:ConnectionString)",
    peachEbillsConfigurado
        ? $", repositorio {(PsaWeb.Modules.Ats.AtsModule.UsaDatosDeMuestra(app.Configuration) ? "DE MUESTRA" : "ODBC / Sage 50")}"
        : "");
app.Logger.LogInformation(
    "Retenciones: módulo {Estado}.",
    peachEbillsConfigurado ? "ACTIVO (PeachEBills configurado)" : "INACTIVO (sin PeachEbills:ConnectionString)");
app.Logger.LogInformation(
    "Plataforma (identidad local): {Estado}.",
    plataformaConfigurada ? "ACTIVA" : "INACTIVA (sin Plataforma:ConnectionString)");
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
                    "Quitá esas variables de entorno.");
            }
            else
            {
                await seeder.CrearSiNoExisteAsync(
                    adminUsuario, adminClave, nombreCompleto: adminUsuario, peachUsername: adminUsuario);
                app.Logger.LogWarning(
                    "Admin inicial {Usuario} creado. Verificá que esté en Plataforma:Admins y " +
                    "quitá Plataforma:AdminInicial:* del entorno.", adminUsuario);
            }
        }
    }
}

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
    app.UseRateLimiter();
}
app.UseAntiforgery();

// Los recursos estáticos (CSS, JS del framework, imágenes) no pasan por la
// política de autenticación: si no, un usuario sin sesión no puede ni ver la
// pantalla de login con estilos.
app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(PsaWeb.Modules.CierreDeCaja.ModuleInfo).Assembly,
        typeof(PsaWeb.Modules.Kardex.ModuleInfo).Assembly,
        typeof(RetencionesModule).Assembly,
        typeof(PsaWeb.Modules.FacturacionElectronica.FacturacionElectronicaModule).Assembly,
        typeof(PsaWeb.Modules.Ats.ModuleInfo).Assembly,
        typeof(PsaWeb.Modules.ConciliacionSri.ModuleInfo).Assembly);

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
    .RequireAuthorization();

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
            return Results.BadRequest("Elegí al menos un ítem, una cuenta o un rango de ítems.");
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
    .RequireAuthorization();

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
    .RequireAuthorization();

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
    .RequireAuthorization();

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

        if (seguridad is not null)
        {
            var empresas = await seguridad.EmpresasDelUsuarioAsync(usuario.UserName ?? string.Empty, cancellationToken);
            if (!empresas.Any(e => e.Ruc == body.Ruc))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
        }

        try
        {
            var resultado = await repositorio.GuardarReporteAsync(body.Ruc, body.ContenidoReporte, usuarioId, cancellationToken);
            return Results.Ok(resultado);
        }
        catch (FormatoReporteInvalidoException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    })
    .AllowAnonymous()
    .AddEndpointFilter<TokenExtensionEndpointFilter>();

app.Run();
