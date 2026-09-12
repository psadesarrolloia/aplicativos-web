using PsaWeb.Ats.Esquema;
using PsaWeb.Ats.Ventas;
using PsaWeb.Ats.Ventas.Muestra;

namespace PsaWeb.Ats.Tests;

public class ArmadorVentasAtsTests
{
    private static ClienteAts ClienteNacional(string id = "1790011110001", bool esParteRelacionada = false) =>
        new(TiposIdentificacionClienteAts.Ruc, id, string.Empty, string.Empty, esParteRelacionada);

    private static ClienteAts ClienteExterior(bool esParteRelacionada = false) =>
        new(TiposIdentificacionClienteAts.Exterior, "20550511941", "02", "CHINA PETROLEUM TECHNOLOGY SUCURSAL PERU", esParteRelacionada);

    private static BucketsVentaAts Buckets(
        decimal baseNoGraIva = 0, decimal baseImponibleCruda = 0, decimal baseImpGrav = 0,
        decimal montoIva = 0, decimal valorRetIva = 0, decimal valorRetRenta = 0) =>
        new(baseNoGraIva, baseImponibleCruda, baseImpGrav, montoIva, valorRetIva, valorRetRenta);

    [Fact]
    public void ArmarDetalle_factura_fija_forma_de_pago_20()
    {
        var fila = new FilaVentaCruda("18", "1", ClienteNacional(), Buckets());

        var detalle = ArmadorVentasAts.ArmarDetalle(fila);

        Assert.Equal(new[] { "20" }, detalle.formasDePago);
    }

    [Fact]
    public void ArmarDetalle_nota_credito_no_lleva_forma_de_pago()
    {
        var fila = new FilaVentaCruda("04", "1", ClienteNacional(), Buckets());

        var detalle = ArmadorVentasAts.ArmarDetalle(fila);

        Assert.Null(detalle.formasDePago);
    }

    [Fact]
    public void ArmarDetalle_bug_B1_baseImponible_es_cero_para_clientes_no_relacionados()
    {
        // Caso real observado en CPTDC julio/2026: la consulta de "gravado 0%"
        // trae 4907.05, pero LoadMontoIVA lo pisa a 0 en el `.exe` — se
        // preserva igual para clientes normales (docs/PLAN-APP3-ATS.md §1.4
        // hallazgo del bug real en LoadSales).
        var fila = new FilaVentaCruda("18", "1", ClienteNacional(), Buckets(baseImponibleCruda: 4907.05m, baseImpGrav: 9484.10m));

        var detalle = ArmadorVentasAts.ArmarDetalle(fila);

        Assert.Equal(0m, detalle.baseImponible);
        Assert.Equal(9484.10m, detalle.baseImpGrav); // este bucket sí llega intacto.
    }

    [Fact]
    public void ArmarDetalle_parte_relacionada_consolida_todo_el_gravado_en_baseImponible()
    {
        // Caso real de CPTDC julio/2026 (cliente 20550511941, RUC peruano,
        // sucursal del mismo grupo): el XML real declarado trae
        // baseImponible=14391.15 (=4907.05+9484.10) y baseImpGrav=0.00 — el
        // Bug B1 NO se preserva para partes relacionadas, es una regla
        // general confirmada por el usuario 2026-09-14, no un ajuste puntual.
        var cliente = ClienteNacional(esParteRelacionada: true);
        var fila = new FilaVentaCruda("18", "1", cliente, Buckets(baseImponibleCruda: 4907.05m, baseImpGrav: 9484.10m));

        var detalle = ArmadorVentasAts.ArmarDetalle(fila);

        Assert.Equal(14391.15m, detalle.baseImponible);
        Assert.Equal(0m, detalle.baseImpGrav);
    }

    [Fact]
    public void ArmarDetalle_cliente_exterior_expone_tipoCliente_y_denoCli()
    {
        var fila = new FilaVentaCruda("18", "1", ClienteExterior(), Buckets());

        var detalle = ArmadorVentasAts.ArmarDetalle(fila);

        Assert.Equal("06", detalle.tpIdCliente);
        Assert.Equal("02", detalle.tipoCliente);
        Assert.Equal("CHINA PETROLEUM TECHNOLOGY SUCURSAL PERU", detalle.denoCli);
    }

    [Fact]
    public void ArmarDetalle_cliente_nacional_no_expone_tipoCliente_ni_denoCli()
    {
        var fila = new FilaVentaCruda("18", "1", ClienteNacional(), Buckets());

        var detalle = ArmadorVentasAts.ArmarDetalle(fila);

        Assert.Null(detalle.tipoCliente);
        Assert.Null(detalle.denoCli);
    }

    [Fact]
    public void ArmarDetalle_fija_parteRelVtas_en_NO_por_defecto()
    {
        var fila = new FilaVentaCruda("18", "1", ClienteNacional(), Buckets());

        var detalle = ArmadorVentasAts.ArmarDetalle(fila);

        Assert.Equal(parteRelType.NO, detalle.parteRelVtas);
    }

    [Fact]
    public void ArmarDetalle_marca_parteRelVtas_SI_si_el_cliente_tiene_el_Sales_Rep_relacionado()
    {
        // Convención acordada con el usuario 2026-09-14: Sage 50 no tiene un
        // campo dedicado para "parte relacionada" — se marca asignándole al
        // cliente un "Sales Rep" cuyo ID/nombre sea "SI RELACIONADO"
        // (docs/PLAN-APP3-ATS.md §3.5a). No usa AccountNumber (ver
        // ClienteAts.EsParteRelacionada).
        var fila = new FilaVentaCruda("18", "1", ClienteNacional(esParteRelacionada: true), Buckets());

        var detalle = ArmadorVentasAts.ArmarDetalle(fila);

        Assert.Equal(parteRelType.SI, detalle.parteRelVtas);
    }

    [Fact]
    public void ArmarDetalle_cliente_exterior_y_relacionado_mantiene_su_propio_tipoCliente()
    {
        // AccountNumber (tipoCliente de clientes del exterior) y el Sales Rep
        // (parte relacionada) son campos independientes — no hay colisión.
        // Caso real de CPTDC: el cliente 20550511941 es exterior Y
        // relacionado, y el XML declarado trae tipoCliente="02" (su valor
        // real) con parteRelVtas="SI" al mismo tiempo.
        var fila = new FilaVentaCruda("18", "1", ClienteExterior(esParteRelacionada: true), Buckets());

        var detalle = ArmadorVentasAts.ArmarDetalle(fila);

        Assert.Equal(parteRelType.SI, detalle.parteRelVtas);
        Assert.Equal("02", detalle.tipoCliente);
    }

    [Fact]
    public void Fusionar_junta_mismo_cliente_y_tipoComprobante_sumando_montos()
    {
        var cliente = ClienteNacional();
        var filas = new[]
        {
            new FilaVentaCruda("18", "3", cliente, Buckets(baseImpGrav: 100m, montoIva: 12m)),
            new FilaVentaCruda("18", "2", cliente, Buckets(baseImpGrav: 50m, montoIva: 6m)),
        };

        var fusionado = Assert.Single(ArmadorVentasAts.Fusionar(filas));

        Assert.Equal(150m, fusionado.baseImpGrav);
        Assert.Equal(18m, fusionado.montoIva);
    }

    [Fact]
    public void Fusionar_bug_B2_concatena_numeroComprobantes_como_texto_en_vez_de_sumar()
    {
        // El esquema declara numeroComprobantes como string y el `.exe` lo
        // fusiona con += (concatena "3"+"2"="32", no suma 5).
        var cliente = ClienteNacional();
        var filas = new[]
        {
            new FilaVentaCruda("18", "3", cliente, Buckets()),
            new FilaVentaCruda("18", "2", cliente, Buckets()),
        };

        var fusionado = Assert.Single(ArmadorVentasAts.Fusionar(filas));

        Assert.Equal("32", fusionado.numeroComprobantes);
    }

    [Fact]
    public void Fusionar_no_junta_clientes_distintos()
    {
        var filas = new[]
        {
            new FilaVentaCruda("18", "1", ClienteNacional("1790011110001"), Buckets(baseImpGrav: 100m)),
            new FilaVentaCruda("18", "1", ClienteNacional("1790022220001"), Buckets(baseImpGrav: 200m)),
        };

        var resultado = ArmadorVentasAts.Fusionar(filas);

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public void Fusionar_no_junta_distinto_tipoComprobante_del_mismo_cliente()
    {
        var cliente = ClienteNacional();
        var filas = new[]
        {
            new FilaVentaCruda("18", "1", cliente, Buckets(baseImpGrav: 100m)),
            new FilaVentaCruda("04", "1", cliente, Buckets(baseImpGrav: 20m)),
        };

        var resultado = ArmadorVentasAts.Fusionar(filas);

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public void TotalVentas_suma_facturas_y_resta_notas_de_credito()
    {
        var detalles = new[]
        {
            ArmadorVentasAts.ArmarDetalle(new FilaVentaCruda("18", "1", ClienteNacional(), Buckets(baseImpGrav: 1000m, baseNoGraIva: 50m))),
            ArmadorVentasAts.ArmarDetalle(new FilaVentaCruda("04", "1", ClienteNacional(), Buckets(baseImpGrav: 100m))),
        };

        var total = ArmadorVentasAts.TotalVentas(detalles);

        Assert.Equal(950m, total); // (1000+50) - 100
    }

    [Fact]
    public void Los_datos_de_muestra_se_fusionan_y_totalizan_sin_reventar()
    {
        var fusionadas = ArmadorVentasAts.Fusionar(VentasMuestraAts.Filas());
        var total = ArmadorVentasAts.TotalVentas(fusionadas);

        Assert.NotEmpty(fusionadas);
        Assert.True(total > 0);
    }
}
