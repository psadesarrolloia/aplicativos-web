using PsaWeb.Ats.Esquema;
using PsaWeb.Ats.Validacion;

namespace PsaWeb.Ats.Tests;

public class ValidadorAtsTests
{
    private static ivaType AtsValido() => new()
    {
        IdInformante = "1792051800001",
        razonSocial = "CPTDC CHINA PETROLEUM TECHNOLOGY y DEVELOPMENT CORPORATION ECUADOR S A",
        Anio = "2026",
        Mes = "07",
        numEstabRuc = "001",
        totalVentas = 1000m,
        totalVentasSpecified = true,
        ventasEstablecimiento = new[] { new ventaEstType { codEstab = "001", ventasEstab = 1000m } },
        compras = new[]
        {
            new detalleComprasType
            {
                establecimiento = "001", puntoEmision = "001", secuencial = "000000001", idProv = "0190000000001",
                fechaRegistro = "15/07/2026", fechaEmision = "15/07/2026",
                baseImpGrav = 100m, montoIva = 15m, valRetBien10 = 10m,
            },
        },
    };

    private static IReadOnlyList<HallazgoAts> Bloqueantes(IReadOnlyList<HallazgoAts> hallazgos) =>
        hallazgos.Where(h => h.Severidad == SeveridadHallazgo.Bloqueante).ToList();

    [Fact]
    public void Ats_valido_no_tiene_bloqueantes_solo_las_2_advertencias_fijas()
    {
        var hallazgos = ValidadorAts.Validar(AtsValido(), 2026, 7);

        Assert.Empty(Bloqueantes(hallazgos));
        Assert.Contains(hallazgos, h => h.Codigo == "MANUAL-PARTE-RELACIONADA");
        Assert.Contains(hallazgos, h => h.Codigo == "MANUAL-RETENCION-SIN-COMPROBANTE");
    }

    [Theory]
    [InlineData("179205180000")] // 12 dígitos
    [InlineData("17920518000AB")] // no numérico
    [InlineData("")]
    public void Ruc_invalido_es_bloqueante(string ruc)
    {
        var ats = AtsValido();
        ats.IdInformante = ruc;
        var hallazgos = ValidadorAts.Validar(ats, 2026, 7);
        Assert.Contains(hallazgos, h => h.Codigo == "EST-RUC" && h.Severidad == SeveridadHallazgo.Bloqueante);
    }

    [Fact]
    public void Razon_social_vacia_es_bloqueante()
    {
        var ats = AtsValido();
        ats.razonSocial = "";
        var hallazgos = ValidadorAts.Validar(ats, 2026, 7);
        Assert.Contains(hallazgos, h => h.Codigo == "EST-RAZON-SOCIAL-VACIA");
    }

    [Fact]
    public void Razon_social_con_caracteres_no_permitidos_es_bloqueante()
    {
        var ats = AtsValido();
        ats.razonSocial = "PAREDES & PAREDES";
        var hallazgos = ValidadorAts.Validar(ats, 2026, 7);
        Assert.Contains(hallazgos, h => h.Codigo == "EST-RAZON-SOCIAL-CARACTERES");
    }

    [Theory]
    [InlineData("00")]
    [InlineData("13")]
    [InlineData("abc")]
    public void Mes_invalido_es_bloqueante(string mes)
    {
        var ats = AtsValido();
        ats.Mes = mes;
        var hallazgos = ValidadorAts.Validar(ats, 2026, 7);
        Assert.Contains(hallazgos, h => h.Codigo == "EST-MES");
    }

    [Fact]
    public void NumEstabRuc_en_000_es_bloqueante()
    {
        var ats = AtsValido();
        ats.numEstabRuc = "000";
        var hallazgos = ValidadorAts.Validar(ats, 2026, 7);
        Assert.Contains(hallazgos, h => h.Codigo == "EST-NUM-ESTAB");
    }

    [Fact]
    public void Cantidad_de_ventasEstablecimiento_distinta_de_numEstabRuc_es_bloqueante()
    {
        var ats = AtsValido();
        ats.numEstabRuc = "002";
        var hallazgos = ValidadorAts.Validar(ats, 2026, 7);
        Assert.Contains(hallazgos, h => h.Codigo == "CRUCE-NUM-ESTAB");
    }

    [Fact]
    public void Suma_de_ventasEstablecimiento_mayor_a_totalVentas_es_bloqueante()
    {
        var ats = AtsValido();
        ats.ventasEstablecimiento = new[] { new ventaEstType { codEstab = "001", ventasEstab = 5000m } };
        var hallazgos = ValidadorAts.Validar(ats, 2026, 7);
        Assert.Contains(hallazgos, h => h.Codigo == "CRUCE-SUMA-VENTAS-ESTAB");
    }

    [Fact]
    public void Compra_sin_ninguna_base_mayor_a_cero_es_bloqueante()
    {
        var ats = AtsValido();
        ats.compras = new[] { new detalleComprasType { establecimiento = "001", puntoEmision = "001", secuencial = "000000001", idProv = "1" } };
        var hallazgos = ValidadorAts.Validar(ats, 2026, 7);
        Assert.Contains(hallazgos, h => h.Codigo == "COMPRA-SIN-BASE");
    }

    [Fact]
    public void Retencion_de_iva_en_compras_mayor_a_montoIva_es_bloqueante()
    {
        var ats = AtsValido();
        ats.compras = new[]
        {
            new detalleComprasType
            {
                establecimiento = "001", puntoEmision = "001", secuencial = "000000001", idProv = "1",
                baseImpGrav = 100m, montoIva = 10m, valRetBien10 = 8m, valRetServ20 = 5m,
            },
        };
        var hallazgos = ValidadorAts.Validar(ats, 2026, 7);
        Assert.Contains(hallazgos, h => h.Codigo == "COMPRA-RETENCION-IVA-MAYOR-MONTO-IVA");
    }

    [Fact]
    public void Fecha_de_compra_con_formato_invalido_es_advertencia()
    {
        var ats = AtsValido();
        ats.compras = new[]
        {
            new detalleComprasType
            {
                establecimiento = "001", puntoEmision = "001", secuencial = "000000001", idProv = "1",
                baseImpGrav = 100m, fechaRegistro = "no-es-fecha", fechaEmision = "15/07/2026",
            },
        };
        var hallazgos = ValidadorAts.Validar(ats, 2026, 7);
        var h = Assert.Single(hallazgos, x => x.Codigo == "COMPRA-FECHA-INVALIDA");
        Assert.Equal(SeveridadHallazgo.Advertencia, h.Severidad);
    }

    [Fact]
    public void Fecha_de_emision_fuera_del_periodo_declarado_es_advertencia()
    {
        var ats = AtsValido();
        ats.compras = new[]
        {
            new detalleComprasType
            {
                establecimiento = "001", puntoEmision = "001", secuencial = "000000001", idProv = "1",
                baseImpGrav = 100m, fechaRegistro = "15/08/2026", fechaEmision = "15/08/2026",
            },
        };
        var hallazgos = ValidadorAts.Validar(ats, 2026, 7);
        Assert.Contains(hallazgos, h => h.Codigo == "COMPRA-FECHA-FUERA-DE-PERIODO");
    }

    [Fact]
    public void Fecha_de_emision_anterior_a_2002_es_advertencia()
    {
        var ats = AtsValido();
        ats.compras = new[]
        {
            new detalleComprasType
            {
                establecimiento = "001", puntoEmision = "001", secuencial = "000000001", idProv = "1",
                baseImpGrav = 100m, fechaRegistro = "15/07/2001", fechaEmision = "15/07/2001",
            },
        };
        var hallazgos = ValidadorAts.Validar(ats, 2001, 7);
        Assert.Contains(hallazgos, h => h.Codigo == "COMPRA-FECHA-MUY-ANTIGUA");
    }

    [Fact]
    public void Compras_duplicadas_por_establecimiento_puntoEmision_secuencial_idProv_es_bloqueante()
    {
        var ats = AtsValido();
        ats.compras = new[]
        {
            new detalleComprasType { establecimiento = "001", puntoEmision = "001", secuencial = "1", idProv = "1", baseImpGrav = 10m },
            new detalleComprasType { establecimiento = "001", puntoEmision = "001", secuencial = "1", idProv = "1", baseImpGrav = 20m },
        };
        var hallazgos = ValidadorAts.Validar(ats, 2026, 7);
        Assert.Contains(hallazgos, h => h.Codigo == "DUP-COMPRA");
    }
}
