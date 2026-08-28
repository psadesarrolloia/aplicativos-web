using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PsaWeb.Host.Auth;

/// <summary>
/// Handler solo para <c>Development</c>: autentica automáticamente como el usuario de
/// Windows de la máquina. PREDATOR no está unido al dominio, así que no hay
/// Kerberos / Negotiate real. En producción se usa
/// <see cref="Microsoft.AspNetCore.Authentication.Negotiate.NegotiateDefaults.AuthenticationScheme"/>
/// contra Active Directory.
/// </summary>
public sealed class DevWindowsAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public DevWindowsAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Environment.UserName;
        if (string.IsNullOrWhiteSpace(user))
        {
            user = "dev";
        }

        var domain = Environment.UserDomainName;
        var name = string.IsNullOrWhiteSpace(domain) ? user : $"{domain}\\{user}";

        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, name) },
            Scheme.Name);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
