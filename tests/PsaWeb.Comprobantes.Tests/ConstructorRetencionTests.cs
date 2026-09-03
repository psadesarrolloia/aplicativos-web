using PsaWeb.Comprobantes.Proveedores;
using PsaWeb.Comprobantes.Retenciones;

namespace PsaWeb.Comprobantes.Tests;

public class ConstructorRetencionTests
{
    private static readonly EmpresaEmisora Emisor = new(
        "1790352897001", "EMPRESA S.A.", "EMPRESA", "Quito", ContribuyenteEspecial: "", ObligadoContabilidad: true);

    private static readonly EstablecimientoInfo Estab = new(7, "001", "002", "Av. Quito 100");

    private static ProveedorSri Proveedor(string tipo = "04") => new()
    {
        Identificacion = "1791709438001",
        TipoIdentificacion = tipo,
        RazonSocial = "PROVEEDOR CIA LTDA",
        Direccion = "Guayaquil",
        Email = "prov@ejemplo.com",
        Errores = Array.Empty<string>(),
    };

    private static DatosCompraRetencion Compra(
        string shipVia = "FACTURA",
        string numRet = "001-002-000000045",
        string numFac = "001-001-000000123",
        DateTime? goodThru = null,
        IReadOnlyList<LineaRetencionCruda>? lineas = null) => new()
    {
        NumeroRetencion = numRet,
        ShipVia = shipVia,
        NumeroFactura = numFac,
        FechaCompra = new DateTime(2026, 8, 10),
        GoodThruDate = goodThru ?? new DateTime(2026, 8, 12),
        PostOrderPeach = "9001",
        Lineas = lineas ?? new[]
        {
            new LineaRetencionCruda("R-RENTA-RF", "312", "1.75%", Amount: 1.75m, Quantity: 100m, "IT-1"),
        },
    };

    private static ResultadoRetencion Construir(
        DatosCompraRetencion? compra = null, ProveedorSri? proveedor = null, bool sinEstablecimiento = false)
        => ConstructorRetencion.Construir(
            Emisor, compra ?? Compra(), proveedor ?? Proveedor(),
            sinEstablecimiento ? null : Estab,
            ambiente: 1, emailDestino: "prov@ejemplo.com", informacionAdicional: null);

    [Fact]
    public void Retencion_valida_arma_el_comprobante_completo()
    {
        var r = Construir();

        Assert.True(r.Ok, string.Join(" | ", r.Errores));
        Assert.NotNull(r.Retencion);
        Assert.Equal("45", r.Retencion!.Secuencial);
        Assert.Equal("08/2026", r.Retencion.PeriodoFiscal);
        Assert.Equal(new DateTime(2026, 8, 12), r.FechaEmision);
        Assert.Equal(7, r.EstablishmentId);
        Assert.Equal("1791709438001", r.Retencion.Sujeto.Identificacion);
        Assert.Equal("001", r.Retencion.Emisor.Establecimiento.Codigo);

        var item = Assert.Single(r.Retencion.Items);
        Assert.Equal("1", item.Codigo);                  // "RF" => renta
        Assert.Equal("312", item.CodigoPorcentaje);
        Assert.Equal(1.75, item.Porcentaje);
        Assert.Equal(100, item.BaseImponible);           // base = Quantity
        Assert.Equal(1.75, item.ValorRetenido);          // retenido = Amount
        Assert.Equal("001-001-000000123", item.NumeroDocumentoSustento);
        Assert.Equal("01", item.TipoDocumentoSustento);
        Assert.Equal(1.75, r.TotalRetenido);
    }

    [Fact]
    public void Item_de_IVA_cuando_la_categoria_no_es_RF()
    {
        var r = Construir(Compra(lineas: new[]
        {
            new LineaRetencionCruda("R-IVA", "725", "100%", Amount: 12m, Quantity: 12m, "IT-2"),
        }));

        Assert.Equal("2", r.Retencion!.Items[0].Codigo);
    }

    [Fact]
    public void GoodThruDate_nula_es_error_y_usa_la_fecha_de_compra()
    {
        var r = Construir(Compra() with { GoodThruDate = null });

        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("Good Thru"));
        Assert.Equal(new DateTime(2026, 8, 10), r.FechaEmision);
    }

    [Fact]
    public void Establecimiento_no_encontrado_es_error()
    {
        var r = Construir(sinEstablecimiento: true);

        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("establecimiento"));
    }

    [Fact]
    public void ShipVia_no_reconocido_es_error()
    {
        var r = Construir(Compra(shipVia: "GUIA"));

        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("no reconocido"));
    }

    [Fact]
    public void Numero_de_factura_mal_formado_es_error()
    {
        var r = Construir(Compra(numFac: "123"));

        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("factura de compra incorrecto"));
    }

    [Fact]
    public void CustomField2_sin_porcentaje_es_error()
    {
        var r = Construir(Compra(lineas: new[]
        {
            new LineaRetencionCruda("R-RENTA-RF", "312", "1.75", Amount: 1.75m, Quantity: 100m, "IT-9"),
        }));

        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("CustomField2") && e.Contains("IT-9"));
    }

    [Fact]
    public void Proveedor_del_exterior_con_factura_es_error()
    {
        var r = Construir(proveedor: Proveedor(tipo: "08"));

        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("tipo de proveedor"));
    }

    [Fact]
    public void Proveedor_del_exterior_con_liquidacion_es_valido()
    {
        var r = Construir(Compra(shipVia: "LIQUIDACION"), Proveedor(tipo: "08"));

        Assert.True(r.Ok, string.Join(" | ", r.Errores));
        Assert.Equal("03", r.Retencion!.Items[0].TipoDocumentoSustento);
    }

    [Fact]
    public void Nota_de_venta_con_valor_retenido_es_error()
    {
        var r = Construir(Compra(shipVia: "NOTA DE VENTA"));

        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("Nota de Venta"));
    }

    [Fact]
    public void Los_errores_del_proveedor_se_propagan()
    {
        var prov = Proveedor();
        var conError = new ProveedorSri
        {
            Identificacion = prov.Identificacion,
            TipoIdentificacion = prov.TipoIdentificacion,
            RazonSocial = prov.RazonSocial,
            Direccion = prov.Direccion,
            Email = prov.Email,
            Errores = new[] { "Email del proveedor no válido: x" },
        };

        var r = Construir(proveedor: conError);

        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("Email del proveedor"));
    }

    [Fact]
    public void InformacionAdicional_se_incluye_cuando_hay()
    {
        var r = ConstructorRetencion.Construir(
            Emisor, Compra(), Proveedor(), Estab, 1, "prov@ejemplo.com",
            new Dictionary<string, string> { ["Observacion"] = "compra local" });

        Assert.Equal("compra local", r.Retencion!.InformacionAdicional!["Observacion"]);
    }
}
