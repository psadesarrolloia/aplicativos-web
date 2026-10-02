using PsaWeb.Ventas.Prefacturas;

namespace PsaWeb.Ventas.Tests;

public class PrefacturaLogicaTests
{
    internal static LineaSolicitada Linea(string item, decimal cantidad, decimal precio, decimal? lista = null, decimal? existencia = 100m) =>
        new(item, "Descripción de " + item, "UND", cantidad, lista ?? precio, precio, existencia);

    internal static SolicitudPrefactura Solicitud(params LineaSolicitada[] lineas) => new(
        "CARLOTA RODRIGUEZ", "RODRIGUEZ FERNANDEZ CARLOTA GABRIELA", "Carlota", "0999999999", "carlota@example.com", 2, 30, 5500500m, 324.76m,
        "WILSON JACHO", "JACHO WILSON (EQU)", "OC-77", "Av. Amazonas y Colón, Quito", "Entrega en 3 días", "Cliente exigente: confirmar stock", true, lineas);

    [Fact]
    public void Reproduce_la_factura_real_creada_por_el_SDK_en_SANCEV()
    {
        // 2 x 6,95 = 13,90 ; 3 x 46,51 = 139,53 ; IVA 15 % de 153,43 = 23,01 ; total 176,44 (factura 999-999-010021624, 2026-10-02).
        var lineas = CalculadoraPrefactura.Calcular(new[] { Linea("RT18Z-32/2P EBAS", 2, 6.95m), Linea("EBS2UZ2P1000VDC", 3, 46.51m) });
        Assert.Equal(new[] { 13.90m, 139.53m }, lineas.Select(l => l.Monto));
        var (subtotal, iva, total) = CalculadoraPrefactura.Totales(lineas, true, 15m);
        Assert.Equal(153.43m, subtotal);
        Assert.Equal(23.01m, iva);
        Assert.Equal(176.44m, total);
    }

    [Theory]
    [InlineData(0.005, 0.01)]
    [InlineData(0.004, 0.00)]
    [InlineData(2.675, 2.68)]
    [InlineData(-0.005, -0.01)]
    public void El_redondeo_es_a_2_decimales_alejandose_de_cero(double entrada, double esperado) =>
        Assert.Equal((decimal)esperado, CalculadoraPrefactura.Redondear((decimal)entrada));

    [Fact]
    public void Sin_IVA_el_total_es_el_subtotal()
    {
        var lineas = CalculadoraPrefactura.Calcular(new[] { Linea("A", 1, 10m) });
        Assert.Equal((10m, 0m, 10m), CalculadoraPrefactura.Totales(lineas, false, 15m));
    }

    [Fact]
    public void El_precio_manual_se_detecta_contra_la_lista()
    {
        Assert.False(Linea("A", 1, 10m, lista: 10m).PrecioManual);
        Assert.True(Linea("A", 1, 9m, lista: 10m).PrecioManual);
        Assert.True(new LineaSolicitada("A", "d", "UND", 1, null, 5m, 1m).PrecioManual);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "Net 1 Day")]
    [InlineData(30, "Net 30 Days")]
    public void Los_terminos_se_escriben_como_los_escribe_Sage(int dias, string esperado) =>
        Assert.Equal(esperado, CalculadoraPrefactura.TerminosSage(dias));

    [Theory]
    [InlineData("DIEGO PONCE", "EQU", "PONCE DIEGO (EQU)")]
    [InlineData("WILSON JACHO", "TAB", "JACHO WILSON (TAB)")]
    [InlineData("WJ TABLEROS", "TAB", "WJ (TAB)")]
    [InlineData("DANIEL CHIRIBOGA", "EQU", "CHIRIBOGA DANIEL (EQU)")]
    [InlineData("MARIA DE LOS ANGELES PEREZ", "EQU", "MARIA DE LOS ANGELES PEREZ (EQU)")]
    [InlineData("", "EQU", "")]
    public void La_etiqueta_sugerida_invierte_nombre_y_apellido_solo_si_son_dos_palabras(string vendedor, string tipo, string esperado) =>
        Assert.Equal(esperado, EtiquetasVendedor.Sugerir(vendedor, tipo));

    [Fact]
    public void Una_solicitud_correcta_no_tiene_errores()
    {
        var r = ValidadorPrefactura.Validar(Solicitud(Linea("A", 2, 10m)), ConfiguracionVentas.PorDefecto("1"));
        Assert.True(r.EsValida);
        Assert.Empty(r.Advertencias);
    }

    [Fact]
    public void Faltan_cliente_vendedor_e_items()
    {
        var s = Solicitud() with { ClienteId = "", Vendedor = " " };
        var r = ValidadorPrefactura.Validar(s, ConfiguracionVentas.PorDefecto("1"));
        Assert.False(r.EsValida);
        Assert.Equal(3, r.Errores.Count);
    }

    [Fact]
    public void Cantidad_cero_o_negativa_es_error_y_precio_cero_solo_advierte()
    {
        var r = ValidadorPrefactura.Validar(Solicitud(Linea("A", 0, 10m), Linea("B", 1, 0m)), ConfiguracionVentas.PorDefecto("1"));
        Assert.Contains(r.Errores, e => e.Contains("cantidad"));
        Assert.Contains(r.Advertencias, a => a.Contains("precio es 0"));
    }

    [Fact]
    public void Pedir_mas_de_la_existencia_y_pasar_el_cupo_solo_advierten()
    {
        var s = Solicitud(Linea("A", 50, 100m, existencia: 10m)) with { LimiteCredito = 1000m, SaldoCliente = 900m };
        var r = ValidadorPrefactura.Validar(s, ConfiguracionVentas.PorDefecto("1"));
        Assert.True(r.EsValida);
        Assert.Contains(r.Advertencias, a => a.Contains("existencia"));
        Assert.Contains(r.Advertencias, a => a.Contains("cupo"));
    }

    [Fact]
    public void Un_item_repetido_advierte()
    {
        var r = ValidadorPrefactura.Validar(Solicitud(Linea("A", 1, 1m), Linea("a", 2, 1m)), ConfiguracionVentas.PorDefecto("1"));
        Assert.Contains(r.Advertencias, a => a.Contains("repetido"));
    }

    [Fact]
    public void Los_destinatarios_se_limpian_y_no_se_repiten()
    {
        var c = ConfiguracionVentas.PorDefecto("1") with { CorreoContabilidad = " conta@x.com ", CorreoAdicional = "CONTA@x.com" };
        Assert.Equal(new[] { "conta@x.com" }, c.Destinatarios());
        Assert.Empty(ConfiguracionVentas.PorDefecto("1").Destinatarios());
        Assert.Equal(2, (c with { CorreoAdicional = "otro@x.com" }).Destinatarios().Count);
    }

    [Fact]
    public void La_vigencia_por_defecto_es_de_15_dias()
    {
        Assert.Equal(15, ConfiguracionVentas.PorDefecto("1").VigenciaDias);
        Assert.Equal("4-15%", ConfiguracionVentas.PorDefecto("1").CodigoImpuesto);
    }
}
