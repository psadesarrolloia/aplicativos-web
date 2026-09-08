using PsaWeb.Comprobantes.Clientes;
using PsaWeb.Comprobantes.Retenciones;
using PsaWeb.Comprobantes.Venta;
using static PsaWeb.Comprobantes.Venta.LectorNotaCredito;

namespace PsaWeb.Comprobantes.Tests;

public class NotaCreditoTests
{
    private static readonly EmpresaEmisora Emisor = new(
        "1791313747001", "SANCEV", "SANCEV", "CHIRIBOGA", "", true);

    private static ClienteSri ClienteOk() => new()
    {
        Identificacion = "1790011110001",
        TipoIdentificacion = "04",
        RazonSocial = "CLIENTE S.A.",
        Direccion = "Quito",
        Email = "cliente@ejemplo.com",
        Errores = Array.Empty<string>(),
    };

    private static FilaCabeceraNc Cab(string reference = "001-003-000000045", decimal main = -112m,
        string? tax = "2-IVA 12%", string? returnAuth = null, string inv = "001-003-000013538")
        => new(reference, main, "42", new DateTime(2026, 9, 5), tax, returnAuth, inv);

    private static RelacionFactura RelOk(string billNumber = "001-003-000013538") =>
        new(true, billNumber, "01", new DateTime(2026, 8, 20), Array.Empty<string>());

    private static FilaLineaNc Linea(decimal qty = 1m, decimal amount = -100m, string desc = "Devolución equipo",
        string tax = "0", string? item = "ART-1") => new(qty, amount, desc, tax, item);

    private static NotaCreditoLeida Armar(
        FilaCabeceraNc? cab = null, RelacionFactura? rel = null,
        decimal? ivaMonto = 12m, decimal? ivaRate1 = 12m,
        IEnumerable<FilaLineaNc>? lineas = null, ClienteSri? cliente = null)
        => ArmarDesde("PO-NC-1", cab ?? Cab(), rel ?? RelOk(), ivaMonto, ivaRate1,
            (lineas ?? new[] { Linea() }).ToList(), cliente ?? ClienteOk());

    [Fact]
    public void NC_simple_con_IVA()
    {
        var r = Armar();

        Assert.True(r.Ok, string.Join(" | ", r.Errores));
        Assert.Equal("001-003-000000045", r.Cabecera.NumeroCompleto);
        Assert.Equal("000000045", r.Cabecera.Secuencial);
        Assert.Equal("001", r.Cabecera.CodigoEstablecimiento);
        Assert.Equal(100, r.Cabecera.TotalSinImpuestos);
        Assert.Equal(12, r.Cabecera.IvaValor);
        Assert.Equal(112, r.Cabecera.TotalConImpuestos);
        Assert.Equal("2", r.Cabecera.CodigoPorcentajeIva);
        Assert.Equal("001-003-000013538", r.DocumentoModificado.BillNumber);
        Assert.Equal("01", r.DocumentoModificado.BillCodeDoc);
        Assert.Equal("Devolución", r.DocumentoModificado.Cause);
        var l = Assert.Single(r.Lineas);
        Assert.Equal(0.12, l.IvaPorcentaje);
        Assert.Equal(12, l.IvaValor);
    }

    [Fact]
    public void NC_toma_la_causa_de_ReturnAuthorization()
    {
        var r = Armar(cab: Cab(returnAuth: "Producto defectuoso"));
        Assert.Equal("Producto defectuoso", r.DocumentoModificado.Cause);
    }

    [Fact]
    public void NC_numero_no_estricto_es_error()
    {
        // formato de factura tolerante NO aplica a NC: 13 chars con guiones no vale
        var r = Armar(cab: Cab(reference: "001-003-45"));
        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("nota de crédito incorrecto"));
    }

    [Fact]
    public void NC_sin_factura_relacionada_es_error()
    {
        var r = Armar(rel: new RelacionFactura(false, "", "01", default, Array.Empty<string>()));
        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("factura de venta relacionada"));
    }

    [Fact]
    public void NC_propaga_errores_de_la_factura_relacionada()
    {
        var r = Armar(rel: new RelacionFactura(true, "001-003-000013538", "01", new DateTime(2026, 8, 20),
            new[] { "Factura relacionada: Número de factura incorrecto." }));
        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("Factura relacionada"));
    }

    [Fact]
    public void NC_tax_invalido_en_linea_es_error()
    {
        var r = Armar(ivaMonto: null, ivaRate1: null, lineas: new[] { Linea(tax: "9") });
        Assert.Contains(r.Errores, e => e.Contains("Valor incorrecto para (Tax)"));
    }

    [Fact]
    public void NC_tax_1_5_6_se_mapean()
    {
        Assert.Equal("0", Armar(ivaMonto: null, ivaRate1: null, lineas: new[] { Linea(tax: "1") }).Lineas[0].CodigoPorcentajeIva);
        Assert.Equal("6", Armar(ivaMonto: null, ivaRate1: null, lineas: new[] { Linea(tax: "5") }).Lineas[0].CodigoPorcentajeIva);
        Assert.Equal("7", Armar(ivaMonto: null, ivaRate1: null, lineas: new[] { Linea(tax: "6") }).Lineas[0].CodigoPorcentajeIva);
    }

    // ---- Constructor + Builder ----

    private static readonly EstablecimientoInfo Estab = new(35, "001", "003", "CHIRIBOGA N50-40");

    [Fact]
    public void Constructor_arma_la_NC_de_Datil_con_documento_modificado()
    {
        var r = ConstructorNotaCredito.Construir(Emisor, Estab, Armar(), ambiente: 1,
            emailPruebas: "pruebas@paredes.com.ec");

        Assert.True(r.Ok, string.Join(" | ", r.Errores));
        var nc = r.NotaCredito!;
        Assert.Equal("45", nc.Secuencial); // sin ceros
        Assert.Equal("001-003-000013538", nc.NumeroDocumentoModificado);
        Assert.Equal("01", nc.TipoDocumentoModificado);
        Assert.Equal("Devolución", nc.Motivo);
        Assert.Equal("2026-08-20T00:00:00-05:00",
            nc.FechaEmisionDocumentoModificado.ToString("yyyy-MM-ddTHH:mm:sszzz"));
        Assert.Equal("003", nc.Emisor.Establecimiento.PuntoEmision); // NC usa el del establecimiento
        Assert.Equal("pruebas@paredes.com.ec", nc.Comprador.Email);
        Assert.Equal(12, nc.Items[0].Impuestos[0].Tarifa); // 0.12 -> 12 (consistencia con factura)
        var impTotal = Assert.Single(nc.Totales.Impuestos);
        Assert.Equal(100, impTotal.BaseImponible);
        Assert.Equal(12, impTotal.Valor);
        Assert.Equal("001-003-000013538", r.Guardar!.DocumentoModificado.BillNumber);
        Assert.Equal("04", r.Guardar.CodDoc);
    }

    private sealed class FakeEstab(EstablecimientoInfo? res) : IEstablecimientoLookup
    {
        public Task<EstablecimientoInfo?> BuscarAsync(string ruc, string codigo, string puntoEmision, CancellationToken ct = default)
            => Task.FromResult(res);
    }

    private sealed class FakeInfo(IReadOnlyDictionary<string, string>? res) : IInfoAdicionalLookup
    {
        public string? CodDocPedido;
        public Task<IReadOnlyDictionary<string, string>?> ObtenerAsync(string ruc, string codDoc, CancellationToken ct = default)
        {
            CodDocPedido = codDoc;
            return Task.FromResult(res);
        }
    }

    [Fact]
    public async Task Builder_resuelve_establecimiento_y_info_adicional_del_codDoc_04()
    {
        var info = new FakeInfo(new Dictionary<string, string> { ["Nota"] = "Reversa" });
        var builder = new NotaCreditoBuilder(new FakeEstab(Estab), info);

        var r = await builder.ArmarAsync(Emisor, Armar(), ambiente: 2, emailPruebas: null);

        Assert.True(r.Ok, string.Join(" | ", r.Errores));
        Assert.Equal("04", info.CodDocPedido);
        Assert.Equal("Reversa", r.NotaCredito!.InformacionAdicional!["Nota"]);
    }

    [Fact]
    public async Task Builder_sin_establecimiento_devuelve_errores()
    {
        var builder = new NotaCreditoBuilder(new FakeEstab(null), new FakeInfo(null));
        var r = await builder.ArmarAsync(Emisor, Armar(), 2, null);

        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("establecimiento"));
    }
}
