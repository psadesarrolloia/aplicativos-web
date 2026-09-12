using PsaWeb.Ats.Esquema;
using PsaWeb.Ats.TalonResumen;

namespace PsaWeb.Ats.Tests;

public class ArmadorTalonResumenAtsTests
{
    private static readonly DateTime Fecha = new(2026, 8, 10, 10, 37, 20);

    private static ivaType Ats(
        detalleComprasType[]? compras = null, detalleVentasType[]? ventas = null, detalleAnuladosType[]? anulados = null) => new()
    {
        IdInformante = "1792051800001",
        razonSocial = "EMPRESA DE PRUEBA S A",
        Anio = "2026",
        Mes = "07",
        compras = compras,
        ventas = ventas,
        anulados = anulados,
    };

    [Fact]
    public void Compras_se_agrupan_por_tipo_en_orden_01_a_04()
    {
        var ats = Ats(compras: new[]
        {
            new detalleComprasType { tipoComprobante = "03", baseImpGrav = 19215m, montoIva = 2882.25m },
            new detalleComprasType { tipoComprobante = "01", baseImponible = 100m, montoIva = 15m },
            new detalleComprasType { tipoComprobante = "01", baseImponible = 50m, montoIva = 7.5m },
        });

        var talon = ArmadorTalonResumenAts.Armar(ats, Fecha);

        Assert.Equal(new[] { "01", "03" }, talon.Compras.Select(f => f.Codigo));
        var factura = talon.Compras.Single(f => f.Codigo == "01");
        Assert.Equal("FACTURA", factura.Transaccion);
        Assert.Equal(2, factura.NumRegistros);
        Assert.Equal(150m, factura.BiTarifa0);
        Assert.Equal(22.5m, factura.ValorIva);
    }

    [Fact]
    public void Total_de_compras_resta_las_notas_de_credito()
    {
        // Caso real de CPTDC julio/2026: 270741.02(01) + 2664.00(02) + 0.00(03) -
        // 110.00(04, NC) = 273295.02 — la suma cruda sin restar la NC daría
        // 273515.02, que NO coincide con el Talón real.
        var ats = Ats(compras: new[]
        {
            new detalleComprasType { tipoComprobante = "01", baseImponible = 270741.02m },
            new detalleComprasType { tipoComprobante = "02", baseImponible = 2664.00m },
            new detalleComprasType { tipoComprobante = "04", baseImponible = 110.00m },
        });

        var talon = ArmadorTalonResumenAts.Armar(ats, Fecha);

        Assert.Equal(273295.02m, talon.TotalCompras.BiTarifa0);
    }

    [Fact]
    public void Ventas_excluye_por_completo_las_notas_de_credito_no_solo_del_total()
    {
        var ats = Ats(ventas: new[]
        {
            new detalleVentasType { tipoComprobante = "18", baseImpGrav = 1000m, montoIva = 150m },
            new detalleVentasType { tipoComprobante = "04", baseImpGrav = 30m, montoIva = 4.5m },
        });

        var talon = ArmadorTalonResumenAts.Armar(ats, Fecha);

        var fila = Assert.Single(talon.Ventas);
        Assert.Equal("18", fila.Codigo);
        Assert.Equal("DOCUMENTOS AUTORIZADOS EN VENTAS EXCEPTO ND Y NC", fila.Transaccion);
        Assert.Equal(1000m, fila.BiTarifaDiferente0);
        Assert.Equal(1000m, talon.TotalVentas.BiTarifaDiferente0);
    }

    [Fact]
    public void Retenciones_de_renta_resuelven_el_concepto_del_catalogo_y_agrupan_por_codigo()
    {
        var ats = Ats(compras: new[]
        {
            new detalleComprasType
            {
                air = new[]
                {
                    new detalleAirComprasType { codRetAir = "303A", baseImpAir = 100m, valRetAir = 5m },
                    new detalleAirComprasType { codRetAir = "303A", baseImpAir = 50m, valRetAir = 2.5m },
                },
            },
        });

        var talon = ArmadorTalonResumenAts.Armar(ats, Fecha);

        var fila = Assert.Single(talon.RetencionesRenta);
        Assert.Equal("SERVICIOS PROFESIONALES PRESTADOS POR SOCIEDADES RESIDENTES", fila.Concepto);
        Assert.Equal(2, fila.NumRegistros);
        Assert.Equal(150m, fila.BaseImponible);
        Assert.Equal(7.5m, fila.ValorRetenido);
    }

    [Fact]
    public void Codigo_de_retencion_desconocido_no_revienta_muestra_el_propio_codigo()
    {
        var ats = Ats(compras: new[]
        {
            new detalleComprasType { air = new[] { new detalleAirComprasType { codRetAir = "999Z", valRetAir = 1m } } },
        });

        var talon = ArmadorTalonResumenAts.Armar(ats, Fecha);

        Assert.Equal("999Z", Assert.Single(talon.RetencionesRenta).Concepto);
    }

    [Fact]
    public void Retencion_de_iva_trae_los_7_buckets_incluido_NC()
    {
        var ats = Ats(compras: new[]
        {
            new detalleComprasType { valRetBien10 = 1m, valRetServ20 = 2m, valorRetBienes = 3m, valRetServ50 = 4m, valorRetServicios = 5m, valRetServ100 = 6m, valorRetencionNc = 7m },
        });

        var talon = ArmadorTalonResumenAts.Armar(ats, Fecha);

        Assert.Equal(7, talon.RetencionesIva.Count);
        Assert.Equal(28m, talon.TotalRetencionesIva);
        Assert.Equal(7m, talon.RetencionesIva.Single(f => f.Concepto == "Retencion IVA NC").ValorRetenido);
    }

    [Fact]
    public void Retenciones_recibidas_en_ventas_suman_iva_y_renta()
    {
        var ats = Ats(ventas: new[]
        {
            new detalleVentasType { tipoComprobante = "18", valorRetIva = 5m, valorRetRenta = 2m },
            new detalleVentasType { tipoComprobante = "18", valorRetIva = 3m, valorRetRenta = 1m },
        });

        var talon = ArmadorTalonResumenAts.Armar(ats, Fecha);

        Assert.Equal(8m, talon.RetencionesRecibidasIva);
        Assert.Equal(3m, talon.RetencionesRecibidasRenta);
        Assert.Equal(11m, talon.TotalRetencionesRecibidas);
    }

    [Fact]
    public void Comprobantes_anulados_es_el_conteo_del_arreglo()
    {
        var ats = Ats(anulados: new[] { new detalleAnuladosType(), new detalleAnuladosType() });
        var talon = ArmadorTalonResumenAts.Armar(ats, Fecha);
        Assert.Equal(2, talon.ComprobantesAnulados);
    }
}
