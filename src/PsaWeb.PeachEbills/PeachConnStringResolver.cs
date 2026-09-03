using Microsoft.EntityFrameworkCore;
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

    public PeachConnStringResolver(IDbContextFactory<PeachEbillsContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<string> ResolverCadenaOdbcAsync(string ruc, CancellationToken cancellationToken = default)
    {
        var row = await LeerAsync(ruc, cancellationToken);
        return Construir(row, DbSecret.Decrypt(row.Pwd));
    }

    public async Task<SageConnectionInfo> ObtenerInfoAsync(string ruc, CancellationToken cancellationToken = default)
    {
        var row = await LeerAsync(ruc, cancellationToken);
        return new SageConnectionInfo(row.Driver, row.Servername, row.Dbq, row.Dsn, row.Uid);
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

    private static string Construir(PeachConnString row, string pwd) =>
        !string.IsNullOrEmpty(row.Servername) && !string.IsNullOrEmpty(row.Dbq)
            ? $"Driver={row.Driver};servername={row.Servername};uid={row.Uid};dbq={row.Dbq};pwd={pwd};"
            : $"Dsn={row.Dsn};Driver={row.Driver};uid={row.Uid};pwd={pwd};";
}
