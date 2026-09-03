using Microsoft.EntityFrameworkCore;
using PsaWeb.PeachEbills;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.PeachEbills.Tests;

public class PeachConnStringResolverTests
{
    private const string LocalConnectionString =
        @"Server=.\SQLEXPRESS;Database=PeachEBills;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3";

    private static bool LocalDbAvailable()
    {
        try
        {
            using var ctx = new PeachEbillsContext(
                new DbContextOptionsBuilder<PeachEbillsContext>().UseSqlServer(LocalConnectionString).Options);
            return ctx.Database.CanConnect();
        }
        catch { return false; }
    }

    private static PeachConnStringResolver CreateResolver()
    {
        var options = new DbContextOptionsBuilder<PeachEbillsContext>()
            .UseSqlServer(LocalConnectionString).Options;
        return new PeachConnStringResolver(new PooledFactory(options));
    }

    [SkippableFact]
    public async Task Arma_la_cadena_ODBC_de_una_empresa_con_servername_y_dbq()
    {
        Skip.IfNot(LocalDbAvailable(), "PeachEBills local no disponible.");

        var resolver = CreateResolver();

        // RUC con servername/dbq y pwd cifrado (verificado en la copia local).
        var cadena = await resolver.ResolverCadenaOdbcAsync("1791741951001");

        Assert.Contains("Driver={Pervasive ODBC Client Interface}", cadena);
        Assert.Contains("servername=SERWEBPSA01", cadena);
        Assert.Contains("dbq=pp20252026", cadena);
        Assert.Contains("uid=Peachtree", cadena);
        Assert.Contains("pwd=JCV1234", cadena);
        Assert.DoesNotContain("pwd=4gOp", cadena); // no queda el valor cifrado
    }

    [SkippableFact]
    public async Task ObtenerInfo_no_expone_la_contraseña()
    {
        Skip.IfNot(LocalDbAvailable(), "PeachEBills local no disponible.");

        var info = await CreateResolver().ObtenerInfoAsync("1791741951001");

        Assert.EndsWith("pwd=***", info.ParaMostrar);
        Assert.True(info.UsaServidor);
    }

    /// <summary>IDbContextFactory mínimo para las pruebas.</summary>
    private sealed class PooledFactory(DbContextOptions<PeachEbillsContext> options)
        : IDbContextFactory<PeachEbillsContext>
    {
        public PeachEbillsContext CreateDbContext() => new(options);
    }
}
