using PsaWeb.Comprobantes.Compras;

namespace PsaWeb.Comprobantes.Tests.Compras;

public class ClasificadorLineasCompraTests
{
    private static readonly BucketsCompra Cero = BucketsCompra.Cero;

    [Fact]
    public void Categoria_IMPUESTO_con_IVA_acumula_montoIva()
    {
        var r = ClasificadorLineasCompra.AcumularLinea(Cero, "IMPUESTO", "IVA", "15%", "", 4.13m, 0.15m);

        Assert.Equal(4.13m, r.MontoIva);
    }

    [Theory]
    // LaborCost va como texto: en Sage viene con muchos decimales de cola
    // ("1,0000000000000000000") — si se pasara como `double` acá, un valor
    // "limpio" como 1.0 perdería el cero decimal al pasar a `decimal` y el
    // .Contains("1.0") del clasificador fallaría por un artificio del test,
    // no por un bug real (así se detectó y se corrigió esta prueba).
    [InlineData("9", "0.1000000000000000000", nameof(BucketsCompra.ValRetBien10))]
    [InlineData("10", "0.2000000000000000000", nameof(BucketsCompra.ValRetServ20))]
    [InlineData("1", "0.3000000000000000000", nameof(BucketsCompra.ValorRetBienes))]
    [InlineData("11", "0.5000000000000000000", nameof(BucketsCompra.ValRetServ50))]
    [InlineData("2", "0.7000000000000000000", nameof(BucketsCompra.ValorRetServicios))]
    [InlineData("3", "1.0000000000000000000", nameof(BucketsCompra.ValRetServ100))]
    public void Categoria_R_IVA_va_al_bucket_segun_CustomField1_y_LaborCost(
        string customField1, string laborCostTexto, string bucketEsperado)
    {
        var laborCost = decimal.Parse(laborCostTexto, System.Globalization.CultureInfo.InvariantCulture);

        // Valores reales observados en CPTDC julio/2026 (Quantity=Amount para
        // simplificar el test; lo que importa es CustomField1 + LaborCost).
        var r = ClasificadorLineasCompra.AcumularLinea(Cero, "R-IVA", customField1, "", "", 100m, laborCost);

        var valor = typeof(BucketsCompra).GetProperty(bucketEsperado)!.GetValue(r);
        Assert.Equal(100m, valor);
    }

    [Fact]
    public void LaborCost_se_compara_con_punto_decimal_sin_importar_la_cultura_activa()
    {
        // Motivo del test: si esto se comparara con la cultura del hilo (en
        // Ecuador, coma decimal), "0,3000..." nunca contendría "0.3" y las
        // retenciones de IVA en compras quedarían siempre en 0 — confirmado
        // que el XML real de CPTDC SÍ trae estos valores.
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("es-EC");

            var r = ClasificadorLineasCompra.AcumularLinea(Cero, "R-IVA", "1", "", "", 100m, 0.3m);

            Assert.Equal(100m, r.ValorRetBienes);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Categoria_R_IRF_no_toca_ningun_bucket()
    {
        var r = ClasificadorLineasCompra.AcumularLinea(Cero, "R-IRF", "310", "", "", 50m, 0.01m);

        Assert.Equal(Cero, r);
    }

    [Theory]
    [InlineData("IMPEXE", nameof(BucketsCompra.BaseImpExe))]
    [InlineData("NOGRAIVA", nameof(BucketsCompra.BaseNoGraIva))]
    [InlineData("IMPONIBLE", nameof(BucketsCompra.BaseImponible))]
    public void Categoria_normal_con_CustomField3_NO_va_al_bucket_de_CustomField4(string customField4, string bucketEsperado)
    {
        var r = ClasificadorLineasCompra.AcumularLinea(Cero, "SERVICIOS", "", "NO", customField4, 300m, 0m);

        var valor = typeof(BucketsCompra).GetProperty(bucketEsperado)!.GetValue(r);
        Assert.Equal(300m, valor);
    }

    [Fact]
    public void Categoria_normal_sin_CustomField3_NO_va_a_baseImpGrav()
    {
        var r = ClasificadorLineasCompra.AcumularLinea(Cero, "SERVICIOS", "", "SI", "", 300m, 0m);

        Assert.Equal(300m, r.BaseImpGrav);
    }

    [Fact]
    public void Redondear_aplica_abs_y_2_decimales_a_todos_los_buckets()
    {
        var crudo = new BucketsCompra(-1.005m, 2.004m, 0m, 0m, 0m, 0, 0, 0, 0, 0, 0);

        var r = ClasificadorLineasCompra.Redondear(crudo);

        Assert.Equal(1.00m, r.BaseNoGraIva); // banker's rounding: 1.005 -> 1.00
        Assert.Equal(2.00m, r.BaseImponible);
    }

    [Fact]
    public void Subtotal_y_Total_se_derivan_de_los_buckets()
    {
        var b = new BucketsCompra(
            BaseNoGraIva: 10m, BaseImponible: 20m, BaseImpGrav: 30m, BaseImpExe: 5m, MontoIva: 6m,
            ValRetBien10: 0, ValRetServ20: 0, ValorRetBienes: 0, ValRetServ50: 0, ValorRetServicios: 0, ValRetServ100: 0);

        Assert.Equal(65m, b.Subtotal); // 10+20+30+5
        Assert.Equal(71m, b.Total);    // Subtotal + IVA
    }
}
