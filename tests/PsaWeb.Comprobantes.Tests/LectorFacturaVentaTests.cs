using PsaWeb.Comprobantes.Clientes;
using PsaWeb.Comprobantes.Venta;
using PsaWeb.Datil.Model;
using static PsaWeb.Comprobantes.Venta.LectorFacturaVenta;

namespace PsaWeb.Comprobantes.Tests;

public class LectorFacturaVentaTests
{
    private static ClienteSri ClienteOk() => new()
    {
        Identificacion = "1790011110001",
        TipoIdentificacion = "04",
        RazonSocial = "CLIENTE S.A.",
        Direccion = "Quito",
        Email = "cliente@ejemplo.com",
        Errores = Array.Empty<string>(),
    };

    private static FilaCabecera Cab(
        string reference = "001-003-000013538", decimal mainAmount = -112m,
        string? taxId = "12", string? taxDesc = "2-IVA 12%",
        DateTime? fecha = null, DateTime? vence = null)
        => new(reference, mainAmount, "42", fecha ?? new DateTime(2026, 9, 4), vence, taxId, taxDesc);

    private static FilaLinea Linea(
        decimal qty = 1m, decimal amount = -100m, string desc = "Servicio",
        string salesTaxType = "0", string? itemId = "ART-1")
        => new(qty, amount, desc, salesTaxType, itemId);

    private static FacturaVentaLeida Armar(
        FilaCabecera? cab = null, FilaDescuento? descuento = null,
        decimal? ivaMonto = 12m, decimal? ivaRate1 = 12m,
        IEnumerable<FilaLinea>? lineas = null, ClienteSri? cliente = null)
        => ArmarDesde("PO1", cab ?? Cab(), descuento, ivaMonto, ivaRate1,
            (lineas ?? new[] { Linea() }).ToList(), cliente ?? ClienteOk(),
            Array.Empty<InfoAdicionalItem>());

    [Fact]
    public void Factura_simple_con_IVA_sin_descuento()
    {
        var r = Armar();

        Assert.True(r.Ok, string.Join(" | ", r.Errores));
        Assert.Equal("001-003-000013538", r.Cabecera.NumeroCompleto);
        Assert.Equal("000013538", r.Cabecera.Secuencial);
        Assert.Equal("001", r.Cabecera.CodigoEstablecimiento);
        Assert.Equal("003", r.Cabecera.PuntoEmision);
        Assert.Equal(112, r.Cabecera.TotalConImpuestos);
        Assert.Equal(12, r.Cabecera.IvaValor);
        Assert.Equal(100, r.Cabecera.TotalSinImpuestos);
        Assert.Equal(100, r.Cabecera.BaseImponibleIva);
        Assert.Equal("2", r.Cabecera.CodigoPorcentajeIva);

        var l = Assert.Single(r.Lineas);
        Assert.Equal(1, l.Cantidad);
        Assert.Equal(100, l.PrecioUnitario);
        Assert.Equal(100, l.SubtotalSinImpuestos);
        Assert.Equal(0.12, l.IvaPorcentaje);
        Assert.Equal(12, l.IvaValor);
        Assert.Equal("2", l.CodigoPorcentajeIva);
        Assert.Equal("ART-1", l.CodigoPrincipal);
        Assert.Equal(0, l.Descuento);
    }

    [Fact]
    public void Factura_sin_IVA_no_calcula_porcentaje()
    {
        var r = Armar(ivaMonto: null, ivaRate1: null,
            lineas: new[] { Linea(salesTaxType: "1", itemId: null) });

        Assert.True(r.Ok, string.Join(" | ", r.Errores));
        Assert.Equal(0, r.Cabecera.IvaValor);
        var l = Assert.Single(r.Lineas);
        Assert.Equal(0, l.IvaValor);
        Assert.Equal(0, l.IvaPorcentaje);
        Assert.Equal("0", l.CodigoPorcentajeIva); // "1" -> "0"
        Assert.Equal("0", l.CodigoPrincipal);      // ItemID null -> "0"
    }

    [Theory]
    [InlineData("5", "6")]
    [InlineData("6", "7")]
    [InlineData("9", "0")]
    public void Mapea_el_codigo_de_porcentaje_de_las_lineas_sin_IVA(string entrada, string esperado)
    {
        var r = Armar(ivaMonto: null, ivaRate1: null,
            lineas: new[] { Linea(salesTaxType: entrada) });

        Assert.Equal(esperado, r.Lineas[0].CodigoPorcentajeIva);
    }

    [Fact]
    public void Descuento_con_IVA_se_resta_de_la_linea_gravada()
    {
        var r = Armar(
            cab: Cab(mainAmount: -100.8m),
            descuento: new FilaDescuento(Amount: 10m, SalesTaxType: 0),
            lineas: new[] { Linea(amount: -100m) });

        var l = Assert.Single(r.Lineas);
        Assert.Equal(90, l.SubtotalSinImpuestos);
        Assert.Equal(90, l.BaseImponibleIva);
        Assert.Equal(10.8, l.IvaValor, 3); // 90 * 0.12
        Assert.Equal(10, l.Descuento);
        Assert.Equal(10, r.Cabecera.DescuentoTotal);
        Assert.Equal(90, r.Cabecera.BaseImponibleIva);
        Assert.True(r.Ok);
    }

    [Fact]
    public void Descuento_sin_IVA_se_resta_de_la_linea_no_gravada()
    {
        var r = Armar(
            ivaMonto: null, ivaRate1: null,
            descuento: new FilaDescuento(Amount: 5m, SalesTaxType: 1),
            lineas: new[] { Linea(amount: -50m, salesTaxType: "1") });

        var l = Assert.Single(r.Lineas);
        Assert.Equal(45, l.SubtotalSinImpuestos);
        Assert.Equal(5, l.Descuento);
        Assert.Equal(5, r.Cabecera.DescuentoTotal);
        Assert.True(r.Ok);
    }

    [Fact]
    public void Descuento_que_no_se_puede_aplicar_es_error()
    {
        // descuento >= subtotal de todas las líneas => queda sin aplicar
        var r = Armar(
            descuento: new FilaDescuento(Amount: 500m, SalesTaxType: 0),
            lineas: new[] { Linea(amount: -100m) });

        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("descuento"));
    }

    [Fact]
    public void Numero_de_factura_invalido_es_error()
    {
        var r = Armar(cab: Cab(reference: "no-es-numero"));

        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("Número de factura"));
    }

    [Fact]
    public void Sin_lineas_es_error()
    {
        var r = Armar(lineas: Array.Empty<FilaLinea>());

        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("detalles"));
    }

    [Fact]
    public void Cantidad_menor_a_uno_se_fuerza_a_uno()
    {
        var r = Armar(lineas: new[] { Linea(qty: 0m, amount: -50m) });

        Assert.Equal(1, r.Lineas[0].Cantidad);
        Assert.Equal(50, r.Lineas[0].PrecioUnitario);
    }

    [Fact]
    public void Cliente_nulo_o_con_errores_deja_la_factura_no_ok()
    {
        var sinCliente = ArmarDesde("PO1", Cab(), null, 12m, 12m,
            new[] { Linea() }.ToList(), cliente: null, Array.Empty<InfoAdicionalItem>());
        Assert.False(sinCliente.Ok);
        Assert.Contains(sinCliente.Errores, e => e.Contains("cliente"));

        var conError = new ClienteSri { Errores = new[] { "Email del cliente no válido: x" } };
        Assert.False(Armar(cliente: conError).Ok);
    }

    [Fact]
    public void Descripcion_multilinea_se_aplana()
    {
        var r = Armar(lineas: new[] { Linea(desc: "Línea 1\r\nLínea 2\nLínea 3") });
        Assert.Equal("Línea 1 Línea 2 Línea 3", r.Lineas[0].Descripcion);
    }

    [Fact]
    public void Numero_corregible_se_normaliza_a_17()
    {
        var r = Armar(cab: Cab(reference: "001-003-13538"));
        Assert.Equal("001-003-000013538", r.Cabecera.NumeroCompleto);
        Assert.Equal("000013538", r.Cabecera.Secuencial);
    }
}
