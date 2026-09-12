using Microsoft.EntityFrameworkCore;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.PeachEbills.Tests;

/// <summary>
/// Verifica que la entidad nueva del ATS (<c>dicIdentityTypeATS</c>) mapea
/// contra la copia local de <c>PeachEBills</c>. Si la base no está, se omite.
/// </summary>
public class DicIdentityTypeAtsTests
{
    private const string LocalConnectionString =
        @"Server=.\SQLEXPRESS;Database=PeachEBills;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3";

    private static PeachEbillsContext CreateContext() =>
        new(new DbContextOptionsBuilder<PeachEbillsContext>().UseSqlServer(LocalConnectionString).Options);

    private static bool LocalDbAvailable()
    {
        try { using var c = CreateContext(); return c.Database.CanConnect(); }
        catch { return false; }
    }

    [SkippableFact]
    public void DicIdentityTypeAts_se_lee_sin_error()
    {
        Skip.IfNot(LocalDbAvailable(), "PeachEBills local no disponible.");

        using var db = CreateContext();

        // Un SELECT: si el mapeo de columnas está mal, tira acá.
        var todos = db.DicIdentityTypeAts.OrderBy(x => x.Id).ToList();
        Assert.NotEmpty(todos);
    }

    [SkippableFact]
    public void TransType_2_compras_mapea_los_codigos_conocidos_de_Vendors()
    {
        Skip.IfNot(LocalDbAvailable(), "PeachEBills local no disponible.");

        using var db = CreateContext();

        // Port de LoadVendor: filtra TransType == 2 y busca por
        // OurAccountWithThem (ProofTypeId acá).
        var compras = db.DicIdentityTypeAts.Where(x => x.TransType == 2).ToList();

        Assert.NotEmpty(compras);
        Assert.All(compras, x =>
        {
            Assert.Equal(2, x.ProofTypeId.Length);
            Assert.Equal(2, x.IdAts.Length);
        });
    }
}
