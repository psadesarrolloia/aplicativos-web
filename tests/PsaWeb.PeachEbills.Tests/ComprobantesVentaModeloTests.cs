using Microsoft.EntityFrameworkCore;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.PeachEbills.Tests;

/// <summary>
/// Verifica que las entidades nuevas de comprobantes de venta (Facturas, Details,
/// NCdetail, Payments, PaymentTypes, FacturaPropiedadExterna,
/// InvoiceConfigAditionalInfo) mapean contra la copia local de <c>PeachEBills</c>.
/// Si la base no está, se omite.
/// </summary>
public class ComprobantesVentaModeloTests
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
    public void Las_7_tablas_de_comprobantes_se_leen_sin_error()
    {
        Skip.IfNot(LocalDbAvailable(), "PeachEBills local no disponible.");

        using var db = CreateContext();

        // Un SELECT TOP 1 por tabla: si el mapeo de columnas está mal, tira aquí.
        _ = db.Facturas.OrderBy(f => f.FacturaId).FirstOrDefault();
        _ = db.Details.OrderBy(d => d.DetailId).FirstOrDefault();
        _ = db.NcDetails.OrderBy(n => n.NCid).FirstOrDefault();
        _ = db.Payments.OrderBy(p => p.PaymentId).FirstOrDefault();
        _ = db.PaymentTypes.OrderBy(p => p.PaymentTypeId).FirstOrDefault();
        _ = db.FacturaPropiedadExterna.OrderBy(x => x.Factura).FirstOrDefault();
        _ = db.InvoiceConfigAditionalInfo.OrderBy(x => x.Id).FirstOrDefault();
    }

    [SkippableFact]
    public void PaymentTypes_tiene_al_menos_una_forma_de_pago_para_Datil()
    {
        Skip.IfNot(LocalDbAvailable(), "PeachEBills local no disponible.");

        using var db = CreateContext();

        var primera = db.PaymentTypes.OrderBy(p => p.PaymentTypeId).FirstOrDefault();

        Skip.If(primera is null, "PaymentTypes vacío en la copia local.");
        Assert.False(string.IsNullOrWhiteSpace(primera!.PaymentDatilCodigo));
    }

    [SkippableFact]
    public void Facturas_del_fixture_SANCEV_se_proyectan()
    {
        Skip.IfNot(LocalDbAvailable(), "PeachEBills local no disponible.");

        using var db = CreateContext();

        // No exige que haya filas todavía (los comprobantes ficticios los crea el
        // área); sólo que la consulta compile y ejecute contra el esquema real.
        var cuantas = db.Facturas.Count(f => f.TransmitterRuc == "1791313747001");
        Assert.InRange(cuantas, 0, int.MaxValue);
    }
}
