using PsaWeb.Ats.Esquema;

namespace PsaWeb.Ats.Tests;

public class ArmadorAtsTests
{
    private static InformacionAts Info(
        IReadOnlyList<string>? establecimientos = null,
        IReadOnlyList<detalleVentasType>? ventas = null,
        IReadOnlyList<ventaEstType>? ventasPorEstablecimiento = null,
        IReadOnlyList<detalleComprasType>? compras = null,
        IReadOnlyList<detalleAnuladosType>? anulados = null) => new(
        Ruc: "1792051800001",
        RazonSocial: "CPTDC CHINA PETROLEUM TECHNOLOGY & DEVELOPMENT CORPORATION ECUADOR S. A.",
        Anio: 2026,
        Mes: 7,
        EstablecimientosActivos: establecimientos ?? Array.Empty<string>(),
        Ventas: ventas ?? Array.Empty<detalleVentasType>(),
        VentasPorEstablecimientoCrudo: ventasPorEstablecimiento ?? Array.Empty<ventaEstType>(),
        Compras: compras ?? Array.Empty<detalleComprasType>(),
        Anulados: anulados ?? Array.Empty<detalleAnuladosType>());

    [Theory]
    [InlineData("CPTDC CHINA PETROLEUM TECHNOLOGY & DEVELOPMENT CORPORATION ECUADOR S. A.",
        "CPTDC CHINA PETROLEUM TECHNOLOGY y DEVELOPMENT CORPORATION ECUADOR S A")]
    [InlineData("PAREDES & PAREDES CIA. LTDA.", "PAREDES y PAREDES CIA LTDA")]
    public void LimpiarRazonSocial_reemplaza_ampersand_y_quita_puntos_y_guiones(string cruda, string esperada)
    {
        // Caso real de CPTDC (verificado byte a byte contra el golden real de
        // julio/2026): "S. A." -> "S A" (el punto se quita, el espacio ya
        // estaba puesto en el nombre crudo de Sage).
        Assert.Equal(esperada, ArmadorAts.LimpiarRazonSocial(cruda));
    }

    [Fact]
    public void Cabecera_se_arma_con_Ruc_Anio_y_Mes_con_2_digitos()
    {
        var ats = ArmadorAts.Armar(Info());

        Assert.Equal("1792051800001", ats.IdInformante);
        Assert.Equal("2026", ats.Anio);
        Assert.Equal("07", ats.Mes);
    }

    [Fact]
    public void Sin_establecimientos_activos_cae_en_001_en_cero()
    {
        var ats = ArmadorAts.Armar(Info());

        Assert.Equal("001", ats.numEstabRuc);
        var unico = Assert.Single(ats.ventasEstablecimiento);
        Assert.Equal("001", unico.codEstab);
        Assert.Equal(0m, unico.ventasEstab);
    }

    [Fact]
    public void Fix_1_usa_los_establecimientos_activos_no_solo_los_que_facturaron()
    {
        // Caso real de CPTDC: 2 establecimientos activos (001 y 002), pero
        // solo 001 facturó en julio/2026 — el `.exe` (sin el fix) reportaría
        // numEstabRuc=001; acá debe reportar 002, con 002 en 0.
        var ats = ArmadorAts.Armar(Info(
            establecimientos: new[] { "001", "002" },
            ventasPorEstablecimiento: new[] { new ventaEstType { codEstab = "001", ventasEstab = 7380817.15m } }));

        Assert.Equal("002", ats.numEstabRuc);
        Assert.Equal(2, ats.ventasEstablecimiento.Length);
        Assert.Equal(7380817.15m, ats.ventasEstablecimiento.Single(v => v.codEstab == "001").ventasEstab);
        Assert.Equal(0m, ats.ventasEstablecimiento.Single(v => v.codEstab == "002").ventasEstab);
    }

    [Fact]
    public void Compras_se_asigna_aunque_este_vacia_ventas_y_anulados_no()
    {
        // Asimetría real del `.exe`: LoadPurchases.ListPurchases() se asigna
        // sin chequear Count, a diferencia de ventas/anulados.
        var ats = ArmadorAts.Armar(Info());

        Assert.NotNull(ats.compras);
        Assert.Empty(ats.compras);
        Assert.Null(ats.ventas);
        Assert.Null(ats.anulados);
    }

    [Fact]
    public void TotalVentas_sale_de_ArmadorVentasAts()
    {
        var ventas = new[]
        {
            new detalleVentasType { tipoComprobante = "18", baseImpGrav = 1000m },
            new detalleVentasType { tipoComprobante = "04", baseImpGrav = 100m },
        };

        var ats = ArmadorAts.Armar(Info(ventas: ventas));

        Assert.Equal(900m, ats.totalVentas);
        Assert.True(ats.totalVentasSpecified);
    }
}
