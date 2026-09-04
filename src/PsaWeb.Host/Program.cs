using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
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
// Producción: Windows Integrated Auth (Negotiate / Kerberos) contra Active Directory.
// Desarrollo (PREDATOR no está en el dominio): un handler firma como el usuario de
// Windows local para poder trabajar sin dominio. La validación real de AD es parte
// de la Fase 5 (promoción a psacontabilidad2).
const string devScheme = "DevWindows";
var isDevelopment = builder.Environment.IsDevelopment();

var authentication = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = isDevelopment ? devScheme : NegotiateDefaults.AuthenticationScheme;
});
authentication.AddNegotiate();
if (isDevelopment)
{
    authentication.AddScheme<AuthenticationSchemeOptions, DevWindowsAuthHandler>(devScheme, _ => { });
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

// En desarrollo, mantené el esquema de PsaWebPlataforma al día automáticamente.
if (plataformaConfigurada && app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PsaWeb.Identidad.IdentidadSeeder>().MigrarAsync();
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

app.MapStaticAssets();
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
