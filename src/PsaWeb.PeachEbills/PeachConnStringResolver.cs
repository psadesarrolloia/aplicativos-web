using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.PeachEbills;

/// <summary>Datos de la conexión ODBC a Sage 50 de una empresa, sin la contraseña.</summary>
public sealed record SageConnectionInfo(string Driver, string? ServerName, string? Dbq, string Dsn, string Uid)
{
    public bool UsaServidor => !string.IsNullOrEmpty(ServerName) && !string.IsNullOrEmpty(Dbq);

    public string ParaMostrar => UsaServidor
        ? $"Driver={Driver};servername={ServerName};uid={Uid};dbq={Dbq};pwd=***"
        : $"Dsn={Dsn};Driver={Driver};uid={Uid};pwd=***";
}

/// <summary>
/// Resuelve, por RUC, la cadena de conexión ODBC a la empresa de Sage 50 desde la
/// tabla <c>PeachConnString</c> (equivalente al <c>PeachCnn</c> original: arma
/// <c>Driver;servername;uid;dbq;pwd</c> o <c>Dsn;Driver;uid;pwd</c> según haya
/// servername/dbq, descifrando <c>pwd</c> con <see cref="DbSecret"/>).
/// </summary>
public sealed class PeachConnStringResolver
{
    private readonly IDbContextFactory<PeachEbillsContext> _contextFactory;
    private readonly string? _serverNameOverride;

    public PeachConnStringResolver(
        IDbContextFactory<PeachEbillsContext> contextFactory,
        IOptions<PeachEbillsOptions>? options = null)
    {
        _contextFactory = contextFactory;
        var o = options?.Value.SageServerNameOverride;
        _serverNameOverride = string.IsNullOrWhiteSpace(o) ? null : o.Trim();
    }

    public async Task<string> ResolverCadenaOdbcAsync(string ruc, CancellationToken cancellationToken = default)
    {
        var row = await LeerAsync(ruc, cancellationToken);
        return Construir(row, DbSecret.Decrypt(row.Pwd), _serverNameOverride);
    }

    public async Task<SageConnectionInfo> ObtenerInfoAsync(string ruc, CancellationToken cancellationToken = default)
    {
        var row = await LeerAsync(ruc, cancellationToken);
        var servername = _serverNameOverride ?? row.Servername;
        return new SageConnectionInfo(row.Driver, servername, row.Dbq, row.Dsn, row.Uid);
    }

    private async Task<PeachConnString> LeerAsync(string ruc, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.PeachConnString
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Ruc == ruc, cancellationToken);

        return row ?? throw new InvalidOperationException(
            $"No hay cadena de conexión ODBC para el RUC {ruc} en la tabla PeachConnString.");
    }

    private static string Construir(PeachConnString row, string pwd, string? serverNameOverride)
    {
        var servername = serverNameOverride ?? row.Servername;
        return !string.IsNullOrEmpty(servername) && !string.IsNullOrEmpty(row.Dbq)
            ? $"Driver={row.Driver};servername={servername};uid={row.Uid};dbq={row.Dbq};pwd={pwd};"
            : $"Dsn={row.Dsn};Driver={row.Driver};uid={row.Uid};pwd={pwd};";
    }
}
