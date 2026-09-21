using PsaWeb.Modules.Reportes.Comun;

namespace PsaWeb.Reportes.Tests;

/// <summary>
/// Casos derivados a mano de la <c>valor_letras</c> del reporte <c>PaymentsProof</c> de Access
/// (con sus rarezas: dobles espacios, «UN MIL», relleno «x» hasta 90, bug Q1).
/// </summary>
public class NumeroALetrasTests
{
    [Fact]
    public void Cero_es_CERO_DOLARES_sin_relleno()
        => Assert.Equal("CERO DOLARES", NumeroALetras.ValorLetras(0m));

    [Fact]
    public void Cien_lleva_centavos_y_relleno_hasta_90_con_espacio_cada_5()
    {
        var r = NumeroALetras.ValorLetras(100m);

        // " CIEN CON 00/100   " mide 19; faltan 71 «x» y se intercala un espacio cada 5 (14 espacios).
        Assert.StartsWith("CIEN CON 00/100   xxxx xxxxx xxxxx", r);
        Assert.Equal(71, r.Count(c => c == 'x'));
        Assert.Equal(103, r.Length); // 19 + 71 + 14 = 104, menos el espacio inicial del Trim
    }

    [Fact]
    public void Mil_doscientos_treinta_y_cuatro_conserva_el_doble_espacio_y_UN_MIL()
    {
        const string prefijo = "UN MIL DOSCIENTOS  TREINTA Y CUATRO CON 56/100   ";
        var r = NumeroALetras.ValorLetras(1234.56m);

        Assert.StartsWith(prefijo + "xxxx x", r);
        Assert.Equal(90 - prefijo.Length, r.Count(c => c == 'x'));
    }

    [Theory]
    [InlineData(15000, "QUINCE MIL CON 00/100")]
    [InlineData(21, "VEINTE Y UN CON 00/100")]
    [InlineData(22, "VEINTIDOS CON 00/100")]
    [InlineData(30, "TREINTA CON 00/100")]
    [InlineData(101.05, "CIENTO UN CON 05/100")]
    [InlineData(999.99, "NOVECIENTOS  NOVENTA Y NUEVE CON 99/100")]
    [InlineData(2000000, "DOS MILLONES  CON 00/100")]
    [InlineData(1000000, "UN MILLON CON 00/100")]
    public void Casos_de_referencia(double monto, string esperadoInicio)
    {
        Assert.StartsWith(esperadoInicio, NumeroALetras.ValorLetras((decimal)monto));
    }

    [Fact]
    public void Millones_y_miles_se_encadenan()
        // 1.500.000,50: millones=1 → "UN MILLON"; miles=500 → " QUINIENTOS MIL".
        => Assert.StartsWith("UN MILLON QUINIENTOS MIL CON 50/100", NumeroALetras.ValorLetras(1500000.50m));

    [Fact]
    public void Diez_con_medio_centavo_usa_redondeo_bancario_como_Round_de_VBA()
        => Assert.StartsWith("DIEZ CON 00/100", NumeroALetras.ValorLetras(10.005m));

    [Fact]
    public void Bug_Q1_montos_menores_a_2_pierden_los_centavos()
    {
        Assert.Equal("UN", NumeroALetras.ValorLetras(1.50m));
        Assert.Equal("UN", NumeroALetras.ValorLetras(1m));
        Assert.Equal("", NumeroALetras.ValorLetras(0.59m));
    }

    [Fact]
    public void Con_la_correccion_los_montos_menores_a_2_llevan_centavos_y_relleno()
    {
        Assert.StartsWith("UN CON 50/100   xxxx x", NumeroALetras.ValorLetras(1.50m, corregirMenoresA2: true));
        Assert.StartsWith("CON 59/100   xxxx x", NumeroALetras.ValorLetras(0.59m, corregirMenoresA2: true));
        // El cero sigue siendo «CERO DOLARES» sin relleno.
        Assert.Equal("CERO DOLARES", NumeroALetras.ValorLetras(0m, corregirMenoresA2: true));
    }

    [Theory]
    [InlineData(0.01, true)]
    [InlineData(1.99, true)]
    [InlineData(2.00, false)]
    [InlineData(0, false)]
    [InlineData(150.25, false)]
    public void PierdeCentavosPorBugQ1_marca_solo_de_0_01_a_1_99(double monto, bool esperado)
        => Assert.Equal(esperado, NumeroALetras.PierdeCentavosPorBugQ1((decimal)monto));

    [Fact]
    public void Un_monto_largo_no_se_rellena_de_mas()
    {
        // Más de 90 caracteres sin relleno: no se agrega ninguna «x».
        var r = NumeroALetras.ValorLetras(987654321.99m);
        Assert.DoesNotContain('x', r);
        Assert.EndsWith("CON 99/100", r.TrimEnd());
    }

    [Fact]
    public void Negativos_y_montos_gigantes_se_rechazan()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NumeroALetras.ValorLetras(-1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => NumeroALetras.ValorLetras(1_000_000_000m));
    }

    [Fact]
    public void El_tamano_del_relleno_es_configurable()
    {
        var r = NumeroALetras.ValorLetras(100m, tamanoRelleno: 40);
        // " CIEN CON 00/100   " = 19 → faltan 21 «x».
        Assert.Equal(21, r.Count(c => c == 'x'));
    }
}
