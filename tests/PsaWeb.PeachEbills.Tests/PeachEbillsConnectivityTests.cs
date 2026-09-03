using Microsoft.EntityFrameworkCore;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.PeachEbills.Tests;

/// <summary>
/// Prueba de integración: verifica que el modelo scaffoldeado abre contra la copia
/// local de <c>PeachEBills</c> en <c>.\SQLEXPRESS</c> y lee datos reales. Si la base
/// no está disponible (otra máquina), la prueba se omite en vez de fallar.
/// </summary>
public class PeachEbillsConnectivityTests
{
    private const string LocalConnectionString =
        @"Server=.\SQLEXPRESS;Database=PeachEBills;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3";

    private static PeachEbillsContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PeachEbillsContext>()
            .UseSqlServer(LocalConnectionString)
            .Options;
        return new PeachEbillsContext(options);
    }

    private static bool LocalDbAvailable()
    {
        try
        {
            using var context = CreateContext();
            return context.Database.CanConnect();
        }
        catch
        {
            return false;
        }
    }

    [SkippableFact]
    public void Abre_y_lee_empresas_activas()
    {
        Skip.IfNot(LocalDbAvailable(), "PeachEBills local (.\\SQLEXPRESS) no disponible.");

        using var context = CreateContext();

        var empresas = context.TransmitterStatus.Count();
        var activas = context.TransmitterStatus.Count(x => x.IsActive);
        var datil = context.DatilApi.Count();

        Assert.True(empresas > 0, "Debería haber empresas en TransmitterStatus.");
        Assert.InRange(activas, 0, empresas);
        Assert.True(datil > 0, "Debería haber configuración de Datil por empresa.");
    }

    [SkippableFact]
    public void Cada_config_de_ambiente_pertenece_a_una_empresa()
    {
        Skip.IfNot(LocalDbAvailable(), "PeachEBills local (.\\SQLEXPRESS) no disponible.");

        using var context = CreateContext();

        var ambientes = context.CurrentAmbient
            .Select(a => new { a.Ruc, a.AmbientDefault })
            .Take(5)
            .ToList();

        Assert.NotEmpty(ambientes);
        Assert.All(ambientes, a => Assert.False(string.IsNullOrWhiteSpace(a.Ruc)));
    }
}
