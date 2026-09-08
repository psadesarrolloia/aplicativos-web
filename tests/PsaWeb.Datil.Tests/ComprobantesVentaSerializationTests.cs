using System.Text.Json;
using PsaWeb.Datil;
using PsaWeb.Datil.Model;

namespace PsaWeb.Datil.Tests;

public class ComprobantesVentaSerializationTests
{
    private static Emisor Emisor() => new()
    {
        Ruc = "1791313747001",
        RazonSocial = "SANCEV ELECTRICA INDUSTRIAL CIA. LTDA.",
        NombreComercial = "SANCEV",
        Direccion = "CHIRIBOGA N50-40 Y MANUEL VALDIVIEZO",
        ObligadoContabilidad = true,
        Establecimiento = new Establecimiento { Codigo = "001", PuntoEmision = "003", Direccion = "CHIRIBOGA" },
    };

    private static Comprador Comprador() => new()
    {
        RazonSocial = "CLIENTE S.A.",
        Identificacion = "1790011110001",
        TipoIdentificacion = "04",
        Email = "cliente@ejemplo.com",
        Direccion = "Quito",
    };

    private static ItemComprobante ItemConIva() => new()
    {
        CodigoPrincipal = "ART-1",
        Descripcion = "Servicio",
        Cantidad = 1,
        PrecioUnitario = 100,
        PrecioTotalSinImpuestos = 100,
        Descuento = 0,
        Impuestos =
        {
            new Impuesto { Codigo = "2", CodigoPorcentaje = "2", BaseImponible = 100, Valor = 12, Tarifa = 12 },
        },
    };

    private static Factura FacturaEjemplo() => new()
    {
        Secuencial = "13538",
        FechaEmision = new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.FromHours(-5)),
        Ambiente = 1,
        Emisor = Emisor(),
        Comprador = Comprador(),
        Items = { ItemConIva() },
        Totales = new TotalesFactura
        {
            TotalSinImpuestos = 100,
            ImporteTotal = 112,
            Propina = 0,
            Descuento = 0,
            Impuestos = { new Impuesto { Codigo = "2", CodigoPorcentaje = "2", BaseImponible = 100, Valor = 12 } },
        },
        Pagos = { new MetodoPago { Medio = "20", Total = 112 } },
    };

    [Fact]
    public void Factura_snake_case_y_estructura()
    {
        var json = DatilJson.Serialize(FacturaEjemplo());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("13538", root.GetProperty("secuencial").GetString());
        Assert.Equal("USD", root.GetProperty("moneda").GetString());
        Assert.Equal(1, root.GetProperty("ambiente").GetInt32());
        Assert.Equal("1.0.0", root.GetProperty("version").GetString());

        var emisor = root.GetProperty("emisor");
        Assert.True(emisor.GetProperty("obligado_contabilidad").GetBoolean());
        Assert.Equal("003", emisor.GetProperty("establecimiento").GetProperty("punto_emision").GetString());

        Assert.Equal("04", root.GetProperty("comprador").GetProperty("tipo_identificacion").GetString());

        var item = root.GetProperty("items")[0];
        Assert.Equal("ART-1", item.GetProperty("codigo_principal").GetString());
        Assert.Equal(100, item.GetProperty("precio_total_sin_impuestos").GetDouble());
        // en el item, el impuesto SÍ lleva tarifa
        Assert.Equal(12, item.GetProperty("impuestos")[0].GetProperty("tarifa").GetDouble());

        // en los totales, el impuesto NO lleva tarifa (null => omitido)
        var impTotal = root.GetProperty("totales").GetProperty("impuestos")[0];
        Assert.False(impTotal.TryGetProperty("tarifa", out _));
        Assert.Equal(12, impTotal.GetProperty("valor").GetDouble());

        Assert.Equal("20", root.GetProperty("pagos")[0].GetProperty("medio").GetString());
    }

    [Fact]
    public void Factura_omite_nulos()
    {
        var json = DatilJson.Serialize(FacturaEjemplo());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.TryGetProperty("guia_remision", out _));
        Assert.False(root.TryGetProperty("clave_acceso", out _));
        Assert.False(root.TryGetProperty("valor_ret_iva", out _));
        Assert.False(root.TryGetProperty("credito", out _));
        Assert.False(root.TryGetProperty("retenciones", out _));
        Assert.False(root.TryGetProperty("info_adicional", out _));
        Assert.False(root.TryGetProperty("informacion_adicional", out _));
    }

    [Fact]
    public void Factura_info_adicional_lista_ordenada_y_diccionario_verbatim()
    {
        var f = FacturaEjemplo();
        f.InfoAdicional = new()
        {
            new InfoAdicionalItem { Nombre = "Orden de compra", Valor = "OC-99" },
            new InfoAdicionalItem { Nombre = "Vendedor", Valor = "Ana" },
        };
        f.InformacionAdicional = new() { ["Email adicional"] = "extra@ejemplo.com" };

        var json = DatilJson.Serialize(f);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var lista = root.GetProperty("info_adicional");
        Assert.Equal("Orden de compra", lista[0].GetProperty("nombre").GetString());
        Assert.Equal("OC-99", lista[0].GetProperty("valor").GetString());
        Assert.Equal("Vendedor", lista[1].GetProperty("nombre").GetString());

        // la clave del diccionario va tal cual (con espacios y mayúsculas)
        Assert.Equal("extra@ejemplo.com",
            root.GetProperty("informacion_adicional").GetProperty("Email adicional").GetString());
    }

    [Fact]
    public void Factura_a_credito_lleva_credito_y_pagos_vacios()
    {
        var f = FacturaEjemplo();
        f.Pagos.Clear();
        f.Credito = new CreditoFactura { Monto = 112, FechaVencimiento = "2026-10-04" };

        var json = DatilJson.Serialize(f);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("2026-10-04", root.GetProperty("credito").GetProperty("fecha_vencimiento").GetString());
        Assert.Equal(112, root.GetProperty("credito").GetProperty("monto").GetDouble());
        Assert.Equal(0, root.GetProperty("pagos").GetArrayLength());
    }

    [Fact]
    public void Factura_fecha_emision_iso_con_offset()
    {
        var json = DatilJson.Serialize(FacturaEjemplo());
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("2026-09-04T00:00:00-05:00", doc.RootElement.GetProperty("fecha_emision").GetString());
    }

    [Fact]
    public void NotaCredito_campos_del_documento_modificado()
    {
        var nc = new NotaCredito
        {
            Secuencial = "45",
            FechaEmision = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.FromHours(-5)),
            FechaEmisionDocumentoModificado = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.FromHours(-5)),
            NumeroDocumentoModificado = "001-003-000013500",
            TipoDocumentoModificado = "01",
            Motivo = "Devolución",
            Emisor = Emisor(),
            Comprador = Comprador(),
            Items = { ItemConIva() },
            Totales = new TotalesNotaCredito
            {
                TotalSinImpuestos = 100,
                ImporteTotal = 112,
                Impuestos = { new Impuesto { Codigo = "2", CodigoPorcentaje = "2", BaseImponible = 100, Valor = 12 } },
            },
        };

        var json = DatilJson.Serialize(nc);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("001-003-000013500", root.GetProperty("numero_documento_modificado").GetString());
        Assert.Equal("01", root.GetProperty("tipo_documento_modificado").GetString());
        Assert.Equal("Devolución", root.GetProperty("motivo").GetString());
        Assert.Equal("2026-08-20T00:00:00-05:00",
            root.GetProperty("fecha_emision_documento_modificado").GetString());
        Assert.False(root.TryGetProperty("clave_acceso", out _));
        Assert.False(root.GetProperty("totales").GetProperty("impuestos")[0].TryGetProperty("tarifa", out _));
    }

    [Fact]
    public void Liquidacion_usa_proveedor_y_forma_pago()
    {
        var liq = new Liquidacion
        {
            Secuencial = "7",
            FechaEmision = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.FromHours(-5)),
            Emisor = Emisor(),
            Proveedor = new Comprador
            {
                RazonSocial = "PROVEEDOR CIA LTDA",
                Identificacion = "0912345678001",
                TipoIdentificacion = "04",
                Email = "prov@ejemplo.com",
                Direccion = "Guayaquil",
            },
            Items = { ItemConIva() },
            Totales = new TotalesLiquidacion
            {
                TotalSinImpuestos = 100,
                ImporteTotal = 112,
                Descuento = 0,
                Impuestos = { new Impuesto { Codigo = "2", CodigoPorcentaje = "2", BaseImponible = 100, Valor = 12 } },
            },
            Pagos = { new FormaPagoLiquidacion { FormaPago = "20", Total = 112 } },
        };

        var json = DatilJson.Serialize(liq);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("PROVEEDOR CIA LTDA", root.GetProperty("proveedor").GetProperty("razon_social").GetString());
        Assert.False(root.TryGetProperty("comprador", out _));
        var pago = root.GetProperty("pagos")[0];
        Assert.Equal("20", pago.GetProperty("forma_pago").GetString());
        Assert.Equal(112, pago.GetProperty("total").GetDouble());
        Assert.False(pago.TryGetProperty("unidad_tiempo", out _));
        Assert.False(pago.TryGetProperty("plazo", out _));
        Assert.False(root.TryGetProperty("guia_remision", out _));
    }
}
