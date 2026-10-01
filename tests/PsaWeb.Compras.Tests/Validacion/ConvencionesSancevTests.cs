using System.Data.Odbc;
using PsaWeb.Compras.Importaciones;

namespace PsaWeb.Compras.Tests.Validacion;

/// <summary>
/// Convenciones de la liquidación de SANCEV (solo lectura, base de SANCEV en <c>PSAWEB_TEST_SAGE_COMPRAS</c> y
/// <c>PSAWEB_TEST_RUC_COMPRAS=1791313747001</c>): cuenta por pagar <c>20000-513</c> y compra con la misma referencia que la OC.
/// </summary>
public class ConvencionesSancevTests
{
    [SkippableFact]
    public async Task Cuenta_por_pagar_y_referencia_de_compra_de_SANCEV()
    {
        Skip.IfNot(EntornoCompras.Ruc == "1791313747001", "Solo con la base de SANCEV (PSAWEB_TEST_RUC_COMPRAS=1791313747001).");
        Skip.If(EntornoCompras.CadenaSage is null, "Sin PSAWEB_TEST_SAGE_COMPRAS.");
        Skip.If(Environment.Is64BitProcess, "El driver ODBC de Pervasive es de 32 bits.");
        await using var cn = new OdbcConnection(EntornoCompras.CadenaSage);
        await cn.OpenAsync();
        Assert.Equal("20000-513", await LectorImportaciones.CuentaPorPagarAsync(cn));
        Assert.False(await LectorImportaciones.CompraConEspacioAsync(cn));
        var oc = await LectorImportaciones.OcPorReferenciaAsync(cn, "LIQ-IMPORT-002-2026", "ZHUHAI TELEHOF ELECT");
        Assert.NotNull(oc);
        Assert.Equal("LIQ-IMPORT-002-2026", oc!.ReferenciaCompra);
        var cuentas = await LectorImportaciones.CuentasAsync(cn);
        Assert.Contains(cuentas, c => c.Cuenta == "13834-340");
        Assert.Equal("002-2026", Liquidaciones.NumeroPropuesto("IMPORTACION-002-2026"));
    }
}
