using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using PsaWeb.Host.Auth;
using PsaWeb.Host.Components;
using PsaWeb.Modules.CierreDeCaja;
using PsaWeb.Sage50;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Acceso a datos de Sage 50 (ODBC / Pervasive). Registrado desde la Fase 1;
// las consultas reales se cablean en la Fase 2.
builder.Services.AddSage50(builder.Configuration);

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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(ModuleInfo).Assembly);

app.Run();
