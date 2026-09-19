using System.Reflection;

namespace PsaWeb.Seguridad.Tests;

/// <summary>
/// Comprobantes electrónicos v2 (F1): los 4 tipos comparten las mismas 4 llaves
/// (ver · hacer · lote · autorizar anulación) y el catálogo las respeta.
/// Sin base de datos: valida constantes y visibilidad del catálogo.
/// </summary>
public class PermisosComprobantesTests
{
    private static ContextoDeUsuario Ctx(params string[] permisos) =>
        new("u", "1790000000001", new HashSet<string>(permisos));

    private static bool Visible(string appId, params string[] permisos) =>
        AppCatalogo.Todas.Single(a => a.Id == appId).VisiblePara(Ctx(permisos));

    [Fact]
    public void Los_codigos_de_comprobantes_son_unicos_y_caben_en_allowCode()
    {
        var codigos = typeof(Permisos)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.Equal(codigos.Count, codigos.Distinct().Count());
        Assert.All(codigos, c => Assert.InRange(c.Length, 1, 10)); // allowAction.allowCode = nvarchar(10) (medido en PeachEBills)
    }

    [Fact]
    public void Cada_tipo_tiene_ver_hacer_lote_y_anular_con_el_patron_de_codigos()
    {
        // (ver, hacer, lote, anular) por tipo — mismo patrón qu*/mk*/…Batch/auCance*.
        var matriz = new[]
        {
            (Permisos.VerFacturas, Permisos.HacerFactura, Permisos.HacerFacturasLote, Permisos.AutorizarAnulacionFactura),
            (Permisos.VerRetenciones, Permisos.HacerRetencion, Permisos.HacerRetencionesLote, Permisos.AutorizarAnulacionRetencion),
            (Permisos.VerNotasCredito, Permisos.HacerNotaCredito, Permisos.HacerNotasCreditoLote, Permisos.AutorizarAnulacionNotaCredito),
            (Permisos.VerLiquidaciones, Permisos.HacerLiquidacion, Permisos.HacerLiquidacionesLote, Permisos.AutorizarAnulacionLiquidacion),
        };

        foreach (var (ver, hacer, lote, anular) in matriz)
        {
            Assert.StartsWith("qu", ver);
            Assert.StartsWith("mk", hacer);
            Assert.EndsWith("Batch", lote);
            Assert.StartsWith("auCance", anular);
        }

        Assert.Equal(16, matriz.SelectMany(m => new[] { m.Item1, m.Item2, m.Item3, m.Item4 }).Distinct().Count());
    }

    [Theory]
    [InlineData("fe-facturas", Permisos.VerFacturas)]
    [InlineData("fe-facturas", Permisos.HacerFactura)]
    [InlineData("fe-facturas", Permisos.HacerFacturasLote)]
    [InlineData("fe-notas-credito", Permisos.VerNotasCredito)]
    [InlineData("fe-notas-credito", Permisos.HacerNotaCredito)]
    [InlineData("fe-notas-credito", Permisos.HacerNotasCreditoLote)]
    [InlineData("fe-liquidaciones", Permisos.VerLiquidaciones)]
    [InlineData("fe-liquidaciones", Permisos.HacerLiquidacion)]
    [InlineData("fe-liquidaciones", Permisos.HacerLiquidacionesLote)]
    [InlineData("retenciones", Permisos.VerRetenciones)]
    [InlineData("retenciones", Permisos.HacerRetencion)]
    [InlineData("retenciones", Permisos.HacerRetencionesLote)]
    public void El_modulo_se_ve_con_cualquiera_de_sus_tres_llaves(string appId, string permiso)
        => Assert.True(Visible(appId, permiso));

    [Theory]
    [InlineData("fe-facturas")]
    [InlineData("fe-notas-credito")]
    [InlineData("fe-liquidaciones")] // ya no es provisional: sin llaves, invisible
    [InlineData("retenciones")]
    public void Sin_llaves_de_comprobantes_el_modulo_no_se_ve(string appId)
        => Assert.False(Visible(appId, Permisos.VerAts));

    [Fact]
    public void Una_llave_de_otro_tipo_no_habilita_el_modulo()
    {
        Assert.False(Visible("fe-liquidaciones", Permisos.VerFacturas, Permisos.HacerNotaCredito));
        Assert.False(Visible("fe-notas-credito", Permisos.VerLiquidaciones));
    }
}
