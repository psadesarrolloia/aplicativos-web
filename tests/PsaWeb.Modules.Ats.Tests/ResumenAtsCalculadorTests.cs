using PsaWeb.Ats.Esquema;
using PsaWeb.Modules.Ats.Data;

namespace PsaWeb.Modules.Ats.Tests;

public class ResumenAtsCalculadorTests
{
    private static ivaType Ats(
        detalleComprasType[]? compras = null,
        detalleVentasType[]? ventas = null,
        detalleAnuladosType[]? anulados = null) => new()
    {
        compras = compras,
        ventas = ventas,
        anulados = anulados,
    };

    [Fact]
    public void Compras_y_ventas_se_agrupan_por_tipoComprobante()
    {
        var ats = Ats(
            compras: new[]
            {
                new detalleComprasType { tipoComprobante = "01", baseImpGrav = 100m, montoIva = 15m },
                new detalleComprasType { tipoComprobante = "01", baseImpGrav = 50m, montoIva = 7.5m },
                new detalleComprasType { tipoComprobante = "03", baseImpGrav = 20m, montoIva = 3m },
            },
            ventas: new[]
            {
                new detalleVentasType { tipoComprobante = "18", baseImpGrav = 1000m, montoIva = 150m },
                new detalleVentasType { tipoComprobante = "04", baseImpGrav = 30m, montoIva = 4.5m },
            });

        var resumen = ResumenAtsCalculador.Calcular(ats);

        Assert.Equal(2, resumen.ComprasPorTipo.Count);
        var facturas = resumen.ComprasPorTipo.Single(c => c.TipoComprobante == "01");
        Assert.Equal(2, facturas.Cantidad);
        Assert.Equal(150m, facturas.BaseImpGrav);
        Assert.Equal(22.5m, facturas.MontoIva);

        Assert.Equal(2, resumen.VentasPorTipo.Count);
        Assert.Contains(resumen.VentasPorTipo, v => v.TipoComprobante == "18" && v.Cantidad == 1);
        Assert.Contains(resumen.VentasPorTipo, v => v.TipoComprobante == "04" && v.Cantidad == 1);
    }

    [Fact]
    public void Retenciones_de_renta_en_compras_se_agrupan_por_codRetAir()
    {
        var ats = Ats(compras: new[]
        {
            new detalleComprasType
            {
                tipoComprobante = "01",
                air = new[]
                {
                    new detalleAirComprasType { codRetAir = "303", baseImpAir = 100m, valRetAir = 1m },
                    new detalleAirComprasType { codRetAir = "303", baseImpAir = 200m, valRetAir = 2m },
                    new detalleAirComprasType { codRetAir = "310", baseImpAir = 50m, valRetAir = 4m },
                },
            },
        });

        var resumen = ResumenAtsCalculador.Calcular(ats);

        Assert.Equal(2, resumen.RetencionesRentaCompras.Count);
        var r303 = resumen.RetencionesRentaCompras.Single(r => r.CodRetAir == "303");
        Assert.Equal(2, r303.Cantidad);
        Assert.Equal(300m, r303.BaseImpAir);
        Assert.Equal(3m, r303.ValRetAir);
    }

    [Fact]
    public void Retenciones_de_iva_en_compras_reporta_los_6_buckets_con_su_suma()
    {
        var ats = Ats(compras: new[]
        {
            new detalleComprasType { valRetBien10 = 10m, valRetServ20 = 20m },
            new detalleComprasType { valorRetBienes = 30m },
        });

        var resumen = ResumenAtsCalculador.Calcular(ats);

        Assert.Equal(6, resumen.RetencionesIvaCompras.Count);
        Assert.Equal(10m, resumen.RetencionesIvaCompras.Single(r => r.Porcentaje == "10%").Valor);
        Assert.Equal(1, resumen.RetencionesIvaCompras.Single(r => r.Porcentaje == "10%").Cantidad);
        Assert.Equal(20m, resumen.RetencionesIvaCompras.Single(r => r.Porcentaje == "20%").Valor);
        Assert.Equal(30m, resumen.RetencionesIvaCompras.Single(r => r.Porcentaje == "30%").Valor);
        Assert.Equal(0m, resumen.RetencionesIvaCompras.Single(r => r.Porcentaje == "50%").Valor);
        Assert.Equal(0, resumen.RetencionesIvaCompras.Single(r => r.Porcentaje == "50%").Cantidad);
    }

    [Fact]
    public void Retenciones_recibidas_en_ventas_suma_iva_y_renta_de_todas_las_filas()
    {
        var ats = Ats(ventas: new[]
        {
            new detalleVentasType { valorRetIva = 5m, valorRetRenta = 2m },
            new detalleVentasType { valorRetIva = 3m, valorRetRenta = 1m },
        });

        var resumen = ResumenAtsCalculador.Calcular(ats);

        Assert.Equal(8m, resumen.RetencionesRecibidasVentas.TotalIva);
        Assert.Equal(3m, resumen.RetencionesRecibidasVentas.TotalRenta);
    }

    [Fact]
    public void Anulados_se_cuentan_por_tipoComprobante()
    {
        var ats = Ats(anulados: new[]
        {
            new detalleAnuladosType { tipoComprobante = "18" },
            new detalleAnuladosType { tipoComprobante = "18" },
            new detalleAnuladosType { tipoComprobante = "04" },
        });

        var resumen = ResumenAtsCalculador.Calcular(ats);

        Assert.Equal(2, resumen.Anulados.Count);
        Assert.Equal(2, resumen.Anulados.Single(a => a.TipoComprobante == "18").Cantidad);
        Assert.Equal(1, resumen.Anulados.Single(a => a.TipoComprobante == "04").Cantidad);
    }

    [Fact]
    public void Sin_datos_devuelve_listas_vacias_sin_reventar()
    {
        var resumen = ResumenAtsCalculador.Calcular(Ats());

        Assert.Empty(resumen.ComprasPorTipo);
        Assert.Empty(resumen.VentasPorTipo);
        Assert.Empty(resumen.RetencionesRentaCompras);
        Assert.Equal(6, resumen.RetencionesIvaCompras.Count);
        Assert.Equal(0m, resumen.RetencionesRecibidasVentas.TotalIva);
        Assert.Empty(resumen.Anulados);
    }
}
