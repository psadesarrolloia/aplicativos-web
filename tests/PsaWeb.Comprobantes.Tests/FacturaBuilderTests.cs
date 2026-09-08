using PsaWeb.Comprobantes.Clientes;
using PsaWeb.Comprobantes.Retenciones;
using PsaWeb.Comprobantes.Venta;
using PsaWeb.Comprobantes.Venta.Muestra;

namespace PsaWeb.Comprobantes.Tests;

public class FacturaBuilderTests
{
    private static readonly EmpresaEmisora Emisor = new(
        "1791313747001", "SANCEV ELECTRICA INDUSTRIAL CIA. LTDA.", "SANCEV", "CHIRIBOGA", "", true);

    private sealed class FakeEstab(EstablecimientoInfo? resultado) : IEstablecimientoLookup
    {
        public string? CodigoPedido;
        public string? PuntoPedido;

        public Task<EstablecimientoInfo?> BuscarAsync(string ruc, string codigo, string puntoEmision, CancellationToken ct = default)
        {
            CodigoPedido = codigo;
            PuntoPedido = puntoEmision;
            return Task.FromResult(resultado);
        }
    }

    [Fact]
    public async Task Resuelve_el_establecimiento_y_arma_la_factura()
    {
        var estab = new FakeEstab(new EstablecimientoInfo(35, "001", "003", "CHIRIBOGA N50-40"));
        var builder = new FacturaBuilder(estab);

        var r = await builder.ArmarAsync(Emisor, FacturasVentaMuestra.Detalle("9001")!, ambiente: 1,
            emailPruebas: "pruebas@paredes.com.ec", codigoFormaPagoDatil: "20");

        Assert.True(r.Ok, string.Join(" | ", r.Errores));
        Assert.Equal("001", estab.CodigoPedido);
        Assert.Equal("003", estab.PuntoPedido);
        Assert.Equal("CHIRIBOGA N50-40", r.Factura!.Emisor.Establecimiento.Direccion);
        Assert.Equal("pruebas@paredes.com.ec", r.Factura.Comprador.Email);
        Assert.Equal(35, r.Guardar!.EstablecimientoId);
    }

    [Fact]
    public async Task Establecimiento_no_encontrado_devuelve_errores()
    {
        var builder = new FacturaBuilder(new FakeEstab(null));

        var r = await builder.ArmarAsync(Emisor, FacturasVentaMuestra.Detalle("9001")!, 2, null, "20");

        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("establecimiento"));
        Assert.Null(r.Factura);
    }

    [Theory]
    [InlineData("9001")]
    [InlineData("9002")]
    [InlineData("9003")]
    public async Task Todas_las_facturas_de_muestra_se_arman(string postOrder)
    {
        var estab = new FakeEstab(new EstablecimientoInfo(35, "001", "003", "s/d"));
        var builder = new FacturaBuilder(estab);

        var r = await builder.ArmarAsync(Emisor, FacturasVentaMuestra.Detalle(postOrder)!, 1, "p@p.ec", "20");

        Assert.True(r.Ok, string.Join(" | ", r.Errores));
        Assert.NotEmpty(r.Factura!.Items);
        Assert.NotNull(r.Guardar);
    }

    [Fact]
    public async Task Factura_de_muestra_con_vencimiento_futuro_va_a_credito()
    {
        var estab = new FakeEstab(new EstablecimientoInfo(35, "001", "001", "s/d"));
        var r = await new FacturaBuilder(estab).ArmarAsync(
            Emisor, FacturasVentaMuestra.Detalle("9003")!, 2, null, "20");

        Assert.True(r.Ok, string.Join(" | ", r.Errores));
        Assert.NotNull(r.Factura!.Credito);
    }
}
