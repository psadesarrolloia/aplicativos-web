using PsaWeb.Modules.Kardex.Data;

namespace PsaWeb.Kardex.Tests;

public class ArmadorKardexTests
{
    private static readonly DateOnly Desde = new(2026, 9, 1);
    private static readonly ItemStock Cs12 = new("CS-012", "Casing 9 5/8", "CASING", "13101");

    private static IReadOnlyList<CostoCrudo> None => Array.Empty<CostoCrudo>();

    private static CostoCrudo Ini(decimal qty, decimal total, decimal opt, DateTime? date = null, long po = 1)
        => new("CS-012", date ?? new DateTime(2026, 8, 20), ".x.", po, total, qty, opt, 3);

    private static CostoCrudo Mov(long tipo, decimal qty, decimal total, decimal opt, long po, string reference = "R")
        => new("CS-012", new DateTime(2026, 9, 10), reference, po, total, qty, opt, tipo);

    private static CostoCrudo Bal(decimal qty, decimal total, decimal opt, long po)
        => new("CS-012", new DateTime(2026, 9, 10), null, po, total, qty, opt, 3);

    [Fact]
    public void Inicial_usa_total_sobre_cantidad_ignorando_optamount_B1()
    {
        // OptAmount 999 debe ser ignorado: costo unitario = 30000 / 1000 = 30.
        var filas = ArmadorKardex.Armar(new[] { Cs12 }, new[] { Ini(1000m, 30_000m, 999m) }, None, None, Desde);

        var f = Assert.Single(filas);
        Assert.True(f.EsInicial);
        Assert.Equal(".INICIAL.", f.Referencia);
        Assert.Equal(Desde, f.Fecha);
        Assert.Equal(1000m, f.Saldo.Cantidad);
        Assert.Equal(30m, f.Saldo.CostoUnitario);
        Assert.Equal(30_000m, f.Saldo.CostoTotal);
        Assert.False(f.Entrada.TieneValor);
        Assert.False(f.Salida.TieneValor);
    }

    [Fact]
    public void Toma_el_inicial_mas_reciente_previo_a_desde()
    {
        var viejo = Ini(100m, 3_000m, 30m, new DateTime(2026, 7, 1), po: 10);
        var nuevo = Ini(120m, 3_720m, 31m, new DateTime(2026, 8, 25), po: 20);

        var filas = ArmadorKardex.Armar(new[] { Cs12 }, new[] { viejo, nuevo }, None, None, Desde);

        Assert.Equal(120m, Assert.Single(filas).Saldo.Cantidad);
    }

    [Fact]
    public void Sin_inicial_no_emite_fila_inicial_pero_si_los_movimientos_B2()
    {
        var mov = Mov(tipo: 1, qty: 50m, total: 1_500m, opt: 30m, po: 5);
        var bal = Bal(150m, 4_500m, 30m, po: 5);

        var filas = ArmadorKardex.Armar(new[] { Cs12 }, None, new[] { mov }, new[] { bal }, Desde);

        var f = Assert.Single(filas);
        Assert.False(f.EsInicial);
        Assert.True(f.Entrada.TieneValor);
    }

    [Fact]
    public void Compra_va_a_entradas_y_venta_a_salidas_con_su_saldo()
    {
        var compra = Mov(tipo: 1, qty: 50m, total: 1_500m, opt: 30m, po: 5, reference: "LIQ IMPORT 007");
        var venta = Mov(tipo: 2, qty: -20m, total: -600m, opt: 0m, po: 6, reference: "001-001-9");
        var balCompra = Bal(qty: 150m, total: 4_500m, opt: 30m, po: 5);
        var balVenta = Bal(qty: 130m, total: 3_900m, opt: 30m, po: 6);

        var filas = ArmadorKardex.Armar(
            new[] { Cs12 }, None, new[] { compra, venta }, new[] { balCompra, balVenta }, Desde);

        Assert.Equal(2, filas.Count);

        var fc = filas[0];
        Assert.Equal("LIQ IMPORT 007", fc.Referencia);
        Assert.Equal(50m, fc.Entrada.Cantidad);
        Assert.False(fc.Salida.TieneValor);
        Assert.Equal(150m, fc.Saldo.Cantidad);

        var fv = filas[1];
        Assert.Equal("001-001-9", fv.Referencia);
        Assert.False(fv.Entrada.TieneValor);
        Assert.Equal(-20m, fv.Salida.Cantidad);
        Assert.Equal(130m, fv.Saldo.Cantidad);
    }

    [Fact]
    public void Compra_recalcula_unitario_solo_si_optamount_igual_al_total_B4()
    {
        // opt == total  => recalcula: 1500 / 50 = 30
        var recal = ArmadorKardex.Armar(new[] { Cs12 }, None,
            new[] { Mov(1, 50m, 1_500m, opt: 1_500m, po: 1) }, None, Desde);
        Assert.Equal(30m, recal[0].Entrada.CostoUnitario);

        // opt != total  => se respeta el opt
        var respeta = ArmadorKardex.Armar(new[] { Cs12 }, None,
            new[] { Mov(1, 50m, 1_500m, opt: 29m, po: 1) }, None, Desde);
        Assert.Equal(29m, respeta[0].Entrada.CostoUnitario);
    }

    [Fact]
    public void Venta_recalcula_unitario_solo_si_optamount_cero_B4()
    {
        var recal = ArmadorKardex.Armar(new[] { Cs12 }, None,
            new[] { Mov(2, -20m, -600m, opt: 0m, po: 1) }, None, Desde);
        Assert.Equal(30m, recal[0].Salida.CostoUnitario); // -600 / -20

        var respeta = ArmadorKardex.Armar(new[] { Cs12 }, None,
            new[] { Mov(2, -20m, -600m, opt: 31m, po: 1) }, None, Desde);
        Assert.Equal(31m, respeta[0].Salida.CostoUnitario);
    }

    [Fact]
    public void Movimiento_sin_snapshot_de_saldo_queda_en_cero()
    {
        var filas = ArmadorKardex.Armar(new[] { Cs12 }, None,
            new[] { Mov(1, 50m, 1_500m, 30m, po: 5) }, None, Desde);

        Assert.Equal(0m, filas[0].Saldo.Cantidad);
        Assert.Equal(0m, filas[0].Saldo.CostoTotal);
    }

    [Fact]
    public void Cantidad_cero_no_divide_por_cero()
    {
        var ini = ArmadorKardex.Armar(new[] { Cs12 }, new[] { Ini(0m, 0m, 0m) }, None, None, Desde);
        Assert.Equal(0m, ini[0].Saldo.CostoUnitario);
    }

    [Fact]
    public void Ordena_movimientos_por_fecha_y_tipo()
    {
        var venta = new CostoCrudo("CS-012", new DateTime(2026, 9, 5), "v", 2, -600m, -20m, 30m, 2);
        var compra = new CostoCrudo("CS-012", new DateTime(2026, 9, 3), "c", 1, 1_500m, 50m, 30m, 1);

        var filas = ArmadorKardex.Armar(new[] { Cs12 }, None, new[] { venta, compra }, None, Desde);

        Assert.Equal("c", filas[0].Referencia);
        Assert.Equal("v", filas[1].Referencia);
    }

    [Fact]
    public void Desempata_por_postorder_cuando_fecha_y_tipo_coinciden()
    {
        // Mismas fecha y MajorType: el orden lo fija PostOrder (determinista,
        // a diferencia del .exe que ahí depende del orden físico de Pervasive).
        var m3 = new CostoCrudo("CS-012", new DateTime(2026, 9, 1), "po3", 3, -300m, -10m, 30m, 2);
        var m1 = new CostoCrudo("CS-012", new DateTime(2026, 9, 1), "po1", 1, -100m, -3m, 30m, 2);
        var m2 = new CostoCrudo("CS-012", new DateTime(2026, 9, 1), "po2", 2, -200m, -6m, 30m, 2);

        var filas = ArmadorKardex.Armar(new[] { Cs12 }, None, new[] { m3, m1, m2 }, None, Desde);

        Assert.Equal(new[] { "po1", "po2", "po3" }, filas.Select(f => f.Referencia));
    }

    [Fact]
    public void Item_sin_datos_no_produce_filas()
    {
        var filas = ArmadorKardex.Armar(new[] { Cs12 }, None, None, None, Desde);
        Assert.Empty(filas);
    }

    [Fact]
    public void Item_con_inicial_en_cero_y_sin_movimientos_se_oculta_si_no_se_incluyen_vacios()
    {
        var ini = new[] { Ini(0m, 0m, 0m) };

        var sinVacios = ArmadorKardex.Armar(new[] { Cs12 }, ini, None, None, Desde, incluirVacios: false);
        Assert.Empty(sinVacios);

        var conVacios = ArmadorKardex.Armar(new[] { Cs12 }, ini, None, None, Desde, incluirVacios: true);
        Assert.Single(conVacios);
    }

    [Fact]
    public void Item_con_inicial_en_cero_pero_con_movimiento_no_se_oculta()
    {
        var filas = ArmadorKardex.Armar(
            new[] { Cs12 }, new[] { Ini(0m, 0m, 0m) },
            new[] { Mov(1, 50m, 1_500m, 30m, po: 5) }, None, Desde, incluirVacios: false);

        Assert.Equal(2, filas.Count);
    }

    [Fact]
    public void Item_con_inicial_no_nulo_no_se_oculta_aunque_sin_movimientos()
    {
        // Cantidad 0 pero costo total != 0: no es "vacío", se muestra.
        var filas = ArmadorKardex.Armar(
            new[] { Cs12 }, new[] { Ini(0m, 0.07m, 70m) }, None, None, Desde, incluirVacios: false);

        Assert.Single(filas);
    }
}
