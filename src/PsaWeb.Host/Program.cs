using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using PsaWeb.Host.Auth;
using PsaWeb.Host.Components;
using PsaWeb.Modules.CierreDeCaja;
using PsaWeb.Modules.CierreDeCaja.Data;
using PsaWeb.Modules.CierreDeCaja.Export;
using PsaWeb.Datil;
using PsaWeb.PeachEbills;
using PsaWeb.Modules.Retenciones;
using PsaWeb.Sage50;
using PsaWeb.Seguridad;
using PsaWeb.Identidad;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Acceso a datos de Sage 50 (ODBC / Pervasive) + módulo Cierre de Caja.
// Sin cadena de conexión configurada, el módulo usa datos de muestra.
builder.Services.AddSage50(builder.Configuration);
builder.Services.AddCierreDeCaja(builder.Configuration);

// Módulo Retenciones (Ola 1). Solo se registra si hay cadena a PeachEBills; sin
// ella la página /retenciones muestra un aviso de "no configurado" y el resto del
// sitio (piloto Cierre de Caja) sigue funcionando igual.
var peachEbillsConfigurado = !string.IsNullOrWhiteSpace(
    builder.Configuration.GetSection(PeachEbillsOptions.SectionName)["ConnectionString"]);
if (peachEbillsConfigurado)
{
    builder.Services.AddPeachEbills(builder.Configuration);
    builder.Services.AddDatil(builder.Configuration);
    builder.Services.AddRetenciones(builder.Configuration);
    // Shell F-Shell-0: directorio de seguridad (empresas + permisos por usuario)
    // y estado de sesión de empresa/ambiente. Registrado, todavía sin pantallas.
    builder.Services.AddSeguridad();
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
        options.LoginPath = "/ingresar";
        options.LogoutPath = "/salir";
        options.AccessDeniedPath = "/ingresar";
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
    "Retenciones: módulo {Estado}.",
    peachEbillsConfigurado ? "ACTIVO (PeachEBills configurado)" : "INACTIVO (sin PeachEbills:ConnectionString)");
app.Logger.LogInformation(
    "Plataforma (identidad local): {Estado}.",
    plataformaConfigurada ? "ACTIVA" : "INACTIVA (sin Plataforma:ConnectionString)");

// En desarrollo, mantené el esquema de PsaWebPlataforma al día y sembrá un
// usuario de prueba (mapeado a un usuario real de PeachEBills para tener empresas
// y permisos). NUNCA en producción.
if (plataformaConfigurada && app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var seeder = scope.ServiceProvider.GetRequiredService<PsaWeb.Identidad.IdentidadSeeder>();
    await seeder.MigrarAsync();

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
        typeof(RetencionesModule).Assembly);

// Descarga del reporte «Cierre de Caja» en Excel. Re-consulta con las mismas
// fechas para que el archivo coincida siempre con lo que se ve en pantalla.
app.MapGet("/cierre-de-caja/export", async (
        DateOnly desde,
        DateOnly hasta,
        ICierreDeCajaRepository repositorio,
        CierreExcelExporter exportador,
        CancellationToken cancellationToken) =>
    {
        if (desde > hasta)
        {
            return Results.BadRequest("La fecha «Desde» no puede ser mayor que «Hasta».");
        }

        var resultado = await repositorio.ObtenerAsync(desde, hasta, cancellationToken);
        var bytes = exportador.Generar(resultado, desde, hasta);

        return Results.File(bytes, CierreExcelExporter.ContentType, exportador.NombreArchivo(desde, hasta));
    })
    .RequireAuthorization();

app.Run();
