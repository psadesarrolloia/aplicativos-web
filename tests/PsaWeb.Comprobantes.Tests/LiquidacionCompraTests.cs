using PsaWeb.Comprobantes.Proveedores;
using PsaWeb.Comprobantes.Retenciones;
using PsaWeb.Comprobantes.Venta;
using static PsaWeb.Comprobantes.Venta.LectorLiquidacionCompra;

namespace PsaWeb.Comprobantes.Tests;

public class LiquidacionCompraTests
{
    private static readonly EmpresaEmisora Emisor = new(
        "1791313747001", "SANCEV", "SANCEV", "CHIRIBOGA", "", true);

    private static readonly EstablecimientoInfo Estab = new(35, "001", "001", "CHIRIBOGA N50-40");

    private static ProveedorSri ProvOk() => new()
    {
        Identificacion = "0912345678001",
        TipoIdentificacion = "04",
        RazonSocial = "PROVEEDOR CIA LTDA",
        Direccion = "Guayaquil",
        Email = "prov@ejemplo.com",
        Errores = Array.Empty<string>(),
    };

    private static FilaCabeceraLiq Cab(string reference = "001-001-000000123", decimal main = -115m,
        DateTime? vence = null)
        => new(reference, main, "77", new DateTime(2026, 9, 3), vence);

    private static FilaItemIva ItemIva(decimal baseAmt = 100m, decimal vat = 15m, string cf3 = "15%")
        => new(baseAmt, vat, "", cf3);

    private static FilaLineaLiq Linea(decimal qty = 1m, decimal amount = 100m, string desc = "Compra bien",
        string cf3 = "", string cf4 = "") => new(qty, amount, desc, cf3, cf4);

    private static readonly TasaIva Tasa15 = new("4", 0.15);

    private static LiquidacionLeida Armar(
        FilaCabeceraLiq? cab = null, IEnumerable<FilaItemIva>? itemsIva = null,
        string? textoPct = "15%", TasaIva? tasa = null,
        IEnumerable<FilaLineaLiq>? lineas = null, ProveedorSri? proveedor = null)
        => ArmarDesde("PO-LIQ-1", cab ?? Cab(),
            (itemsIva ?? new[] { ItemIva() }).ToList(), textoPct, tasa ?? Tasa15,
            (lineas ?? new[] { Linea() }).ToList(), proveedor ?? ProvOk());

    [Fact]
    public void Liquidacion_simple_con_IVA_15()
    {
        var r = Armar();

        Assert.True(r.Ok, string.Join(" | ", r.Errores));
        Assert.Equal("001-001-000000123", r.Cabecera.NumeroCompleto);
        Assert.Equal("4", r.Cabecera.CodigoPorcentajeIva);
        Assert.Equal(100, r.Cabecera.TotalSinImpuestos);
        Assert.Equal(15, r.Cabecera.IvaValor);
        Assert.Equal(115, r.Cabecera.TotalConImpuestos);
        var l = Assert.Single(r.Lineas);
        Assert.Equal(0.15, l.IvaPorcentaje);
        Assert.Equal(15, l.IvaValor);
        Assert.Equal("4", l.CodigoPorcentajeIva);
        Assert.Equal("NoCode", l.CodigoPrincipal);
    }

    [Fact]
    public void Mas_de_un_item_de_IVA_es_error()
    {
        var r = Armar(itemsIva: new[] { ItemIva(), ItemIva(cf3: "12%") });
        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("más de un ítem de IVA"));
    }

    [Fact]
    public void Item_de_IVA_sin_porcentaje_es_error()
    {
        var r = Armar(itemsIva: new[] { new FilaItemIva(100m, 0m, "algo", "otra cosa") }, textoPct: null, tasa: null);
        Assert.Contains(r.Errores, e => e.Contains("mal configurado"));
    }

    [Fact]
    public void Porcentaje_no_encontrado_en_dicTaxRate_es_error()
    {
        var r = ArmarDesde("PO", Cab(), new[] { ItemIva(cf3: "13%") }, "13%", tasa: null,
            new[] { Linea() }, ProvOk());
        Assert.Contains(r.Errores, e => e.Contains("código de IVA"));
        Assert.Equal("4", r.Cabecera.CodigoPorcentajeIva); // default del .exe
    }

    [Fact]
    public void Linea_NO_IVA_queda_en_cero_y_mapea_por_CustomField4()
    {
        var r = Armar(lineas: new[]
        {
            Linea(amount: 50m, cf3: "IVA NO OBJETO", cf4: "IMPEX-XX"),
        });

        var l = Assert.Single(r.Lineas);
        Assert.Equal("0", l.CodigoPorcentajeIva);
        Assert.Equal(0, l.IvaPorcentaje);
        Assert.Equal(0, l.IvaValor);
        // el código "actual" muta a "7" (IMPEX) y así queda en la cabecera (port fiel)
        Assert.Equal("7", r.Cabecera.CodigoPorcentajeIva);
    }

    [Fact]
    public void Solo_toma_lineas_con_Amount_positivo()
    {
        var r = Armar(lineas: new[]
        {
            Linea(amount: 100m, desc: "bien"),
            Linea(amount: -20m, desc: "descuento"),
        });
        Assert.Single(r.Lineas);
        Assert.Equal("bien", r.Lineas[0].Descripcion);
    }

    [Fact]
    public void Numero_invalido_y_proveedor_nulo_son_errores()
    {
        Assert.Contains(Armar(cab: Cab(reference: "xx")).Errores, e => e.Contains("Número de factura"));
        var r = ArmarDesde("PO", Cab(), new[] { ItemIva() }, "15%", Tasa15, new[] { Linea() }, proveedor: null);
        Assert.Contains(r.Errores, e => e.Contains("proveedor"));
    }

    // ---- Constructor + Builder ----

    [Fact]
    public void Constructor_arma_la_liquidacion_de_Datil()
    {
        var r = ConstructorLiquidacion.Construir(Emisor, Estab, Armar(), ambiente: 1,
            emailPruebas: "pruebas@paredes.com.ec");

        Assert.True(r.Ok, string.Join(" | ", r.Errores));
        var liq = r.Liquidacion!;
        Assert.Equal("123", liq.Secuencial);
        Assert.Equal("001", liq.Emisor.Establecimiento.PuntoEmision); // del número
        Assert.Equal("PROVEEDOR CIA LTDA", liq.Proveedor.RazonSocial);
        Assert.Equal("pruebas@paredes.com.ec", liq.Proveedor.Email);
        var pago = Assert.Single(liq.Pagos);
        Assert.Equal("20", pago.FormaPago);
        Assert.Equal(115, pago.Total);
        Assert.Equal(15, liq.Items[0].Impuestos[0].Tarifa); // 0.15 -> 15
        Assert.Equal("03", r.Guardar!.CodDoc);
        Assert.Equal(2, r.Guardar.TransType);
    }

    private sealed class FakeEstab(EstablecimientoInfo? res) : IEstablecimientoLookup
    {
        public Task<EstablecimientoInfo?> BuscarAsync(string ruc, string codigo, string puntoEmision, CancellationToken ct = default)
            => Task.FromResult(res);
    }

    private sealed class FakeInfo(IReadOnlyDictionary<string, string>? res) : IInfoAdicionalLookup
    {
        public string? CodDoc;
        public Task<IReadOnlyDictionary<string, string>?> ObtenerAsync(string ruc, string codDoc, CancellationToken ct = default)
        {
            CodDoc = codDoc;
            return Task.FromResult(res);
        }
    }

    [Fact]
    public async Task Builder_resuelve_establecimiento_e_info_del_codDoc_03()
    {
        var info = new FakeInfo(new Dictionary<string, string> { ["Ref"] = "OC-9" });
        var r = await new LiquidacionBuilder(new FakeEstab(Estab), info)
            .ArmarAsync(Emisor, Armar(), ambiente: 2, emailPruebas: null);

        Assert.True(r.Ok, string.Join(" | ", r.Errores));
        Assert.Equal("03", info.CodDoc);
        Assert.Equal("OC-9", r.Liquidacion!.InformacionAdicional!["Ref"]);
    }

    [Fact]
    public async Task Builder_sin_establecimiento_devuelve_errores()
    {
        var r = await new LiquidacionBuilder(new FakeEstab(null), new FakeInfo(null))
            .ArmarAsync(Emisor, Armar(), 2, null);
        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("establecimiento"));
    }
}
