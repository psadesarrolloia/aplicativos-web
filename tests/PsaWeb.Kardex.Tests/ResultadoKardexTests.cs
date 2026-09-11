using PsaWeb.Modules.Kardex.Data;

namespace PsaWeb.Kardex.Tests;

public class ResultadoKardexTests
{
    private static readonly DateOnly D = new(2026, 9, 1);

    private static MovimientoKardex Sdo(decimal costoTotal) => new(1m, 1m, costoTotal);

    private static FilaKardex Fila(string item, string cuenta, decimal saldo, bool esInicial = false) => new(
        cuenta, item, $"Nombre {item}", "CASING", D, esInicial ? ".INICIAL." : "R",
        MovimientoKardex.Vacio, MovimientoKardex.Vacio, Sdo(saldo), esInicial);

    [Fact]
    public void Toma_la_ultima_fila_de_cada_item_como_saldo_final()
    {
        // CS-012 tiene 3 filas; el resumen debe usar sólo la última (300, no 100+200+300).
        var r = new ResultadoKardex(new[]
        {
            Fila("CS-012", "13101", 100m, esInicial: true),
            Fila("CS-012", "13101", 200m),
            Fila("CS-012", "13101", 300m),
        });

        var resumen = Assert.Single(r.ResumenPorCuenta());
        Assert.Equal("13101", resumen.CuentaGl);
        Assert.Equal(300m, resumen.SaldoFinal);
        Assert.Equal(300m, r.TotalGeneral());
    }

    [Fact]
    public void Suma_el_saldo_final_de_cada_item_dentro_de_la_misma_cuenta()
    {
        var r = new ResultadoKardex(new[]
        {
            Fila("CS-001", "13101", 100m, esInicial: true),
            Fila("CS-012", "13101", 50m, esInicial: true),
            Fila("CS-012", "13101", 250m), // saldo final real de CS-012
        });

        var resumen = Assert.Single(r.ResumenPorCuenta());
        Assert.Equal(350m, resumen.SaldoFinal); // 100 (CS-001) + 250 (CS-012, no 50)
    }

    [Fact]
    public void Agrupa_por_cuenta_y_ordena_alfabeticamente()
    {
        var r = new ResultadoKardex(new[]
        {
            Fila("CW-001", "13103", 500m, esInicial: true),
            Fila("CS-001", "13101", 100m, esInicial: true),
        });

        var cuentas = r.ResumenPorCuenta().Select(x => x.CuentaGl).ToList();
        Assert.Equal(new[] { "13101", "13103" }, cuentas);
        Assert.Equal(600m, r.TotalGeneral());
    }

    [Fact]
    public void Item_sin_cuenta_resuelta_va_a_sin_cuenta()
    {
        var r = new ResultadoKardex(new[] { Fila("X-001", "", 10m, esInicial: true) });

        var resumen = Assert.Single(r.ResumenPorCuenta());
        Assert.Equal("(sin cuenta)", resumen.CuentaGl);
    }

    [Fact]
    public void Sin_filas_el_resumen_y_el_total_son_cero()
    {
        Assert.Empty(ResultadoKardex.Vacio.ResumenPorCuenta());
        Assert.Equal(0m, ResultadoKardex.Vacio.TotalGeneral());
    }
}
