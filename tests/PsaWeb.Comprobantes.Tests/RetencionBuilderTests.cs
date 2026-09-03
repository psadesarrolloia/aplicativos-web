using PsaWeb.Comprobantes.Proveedores;
using PsaWeb.Comprobantes.Retenciones;

namespace PsaWeb.Comprobantes.Tests;

public class RetencionBuilderTests
{
    private static readonly EmpresaEmisora Emisor = new(
        "1790352897001", "EMPRESA S.A.", "EMPRESA", "Quito", "", true);

    private static DatosCompraRetencion Compra(string numRet = "001-002-000000045") => new()
    {
        NumeroRetencion = numRet,
        ShipVia = "FACTURA",
        NumeroFactura = "001-001-000000123",
        FechaCompra = new DateTime(2026, 8, 10),
        GoodThruDate = new DateTime(2026, 8, 12),
        PostOrderPeach = "9001",
        Lineas = new[]
        {
            new LineaRetencionCruda("R-RENTA-RF", "312", "1.75%", 1.75m, 100m, "IT-1"),
        },
    };

    private static ProveedorSri Proveedor() => new()
    {
        Identificacion = "1791709438001",
        TipoIdentificacion = "04",
        RazonSocial = "PROVEEDOR CIA LTDA",
        Direccion = "Guayaquil",
        Email = "real@proveedor.com",
        Errores = Array.Empty<string>(),
    };

    private sealed class FakeEstab(EstablecimientoInfo? resultado) : IEstablecimientoLookup
    {
        public string? RucPedido; public string? CodigoPedido; public string? PuntoPedido;
        public Task<EstablecimientoInfo?> BuscarAsync(string ruc, string codigo, string puntoEmision, CancellationToken ct = default)
        {
            RucPedido = ruc; CodigoPedido = codigo; PuntoPedido = puntoEmision;
            return Task.FromResult(resultado);
        }
    }

    private sealed class FakeInfo(IReadOnlyDictionary<string, string>? resultado) : IInfoAdicionalLookup
    {
        public Task<IReadOnlyDictionary<string, string>?> ObtenerAsync(string ruc, string codDoc, CancellationToken ct = default)
            => Task.FromResult(resultado);
    }

    [Fact]
    public async Task Resuelve_el_establecimiento_por_el_numero_de_retencion()
    {
        var estab = new FakeEstab(new EstablecimientoInfo(7, "001", "002", "Av. Quito 100"));
        var builder = new RetencionBuilder(estab, new FakeInfo(null));

        var r = await builder.ArmarAsync(Emisor, Compra(), Proveedor(), ambiente: 2);

        Assert.True(r.Ok, string.Join(" | ", r.Errores));
        Assert.Equal("1790352897001", estab.RucPedido);
        Assert.Equal("001", estab.CodigoPedido);
        Assert.Equal("002", estab.PuntoPedido);
        Assert.Equal(7, r.EstablishmentId);
    }

    [Fact]
    public async Task En_ambiente_de_pruebas_usa_el_email_de_pruebas()
    {
        var builder = new RetencionBuilder(
            new FakeEstab(new EstablecimientoInfo(7, "001", "002", "x")), new FakeInfo(null));

        var r = await builder.ArmarAsync(Emisor, Compra(), Proveedor(), ambiente: 1, emailPruebas: "pruebas@psa.com");

        Assert.Equal("pruebas@psa.com", r.Retencion!.Sujeto.Email);
    }

    [Fact]
    public async Task En_produccion_usa_el_email_real_del_proveedor()
    {
        var builder = new RetencionBuilder(
            new FakeEstab(new EstablecimientoInfo(7, "001", "002", "x")), new FakeInfo(null));

        var r = await builder.ArmarAsync(Emisor, Compra(), Proveedor(), ambiente: 2, emailPruebas: "pruebas@psa.com");

        Assert.Equal("real@proveedor.com", r.Retencion!.Sujeto.Email);
    }

    [Fact]
    public async Task Sin_establecimiento_registrado_da_error()
    {
        var builder = new RetencionBuilder(new FakeEstab(null), new FakeInfo(null));

        var r = await builder.ArmarAsync(Emisor, Compra(), Proveedor(), ambiente: 2);

        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("establecimiento"));
    }

    [Fact]
    public async Task La_informacion_adicional_llega_al_comprobante()
    {
        var builder = new RetencionBuilder(
            new FakeEstab(new EstablecimientoInfo(7, "001", "002", "x")),
            new FakeInfo(new Dictionary<string, string> { ["Contrato"] = "C-2026-08" }));

        var r = await builder.ArmarAsync(Emisor, Compra(), Proveedor(), ambiente: 2);

        Assert.Equal("C-2026-08", r.Retencion!.InformacionAdicional!["Contrato"]);
    }
}
