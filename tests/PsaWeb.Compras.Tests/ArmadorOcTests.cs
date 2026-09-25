using PsaWeb.Compras.Armado;
using PsaWeb.Compras.Catalogo;
using PsaWeb.Compras.Sri;

namespace PsaWeb.Compras.Tests;

public class ArmadorOcTests
{
    private static readonly CatalogoCompras Catalogo = CatalogoPrueba.Crear();
    private const string Clave = "0101202601179999999900120010020000001231234567811";

    private static LineaDetalle Linea(string descripcion, decimal monto, string codigoIva = "4", decimal tarifa = 15, decimal cantidad = 1,
        string? rf = null, string? riva = null, string cuenta = "60505") => new()
    {
        Descripcion = descripcion, Cantidad = cantidad, MontoSinImpuestos = monto, PrecioUnitario = monto / cantidad,
        CodigoIva = "2", CodigoPorcentajeIva = codigoIva, TarifaIva = tarifa, CuentaId = cuenta,
        RetencionFuenteId = rf, RetencionIvaId = riva,
    };

    private static LineaImpuesto Iva(decimal baseImponible, decimal valor) => new()
    {
        Codigo = "2", BaseImponible = baseImponible, Valor = valor, ItemId = "15% IVA COMPRAS", CuentaId = "14233",
    };

    private static EntradaCompra Entrada(IReadOnlyList<LineaDetalle> detalles, IReadOnlyList<LineaImpuesto> impuestos, int formaPago,
        decimal propina = 0, TipoDocumentoCompra tipo = TipoDocumentoCompra.Factura, ProveedorSage? proveedor = null, string numeroOc = "OC-8757")
    {
        var prov = proveedor ?? CatalogoPrueba.Proveedor();
        return new EntradaCompra
        {
            RucEmpresa = "1799999998001",
            TipoDocumento = tipo,
            Sustento = SustentoCompra.Credito,
            Origen = OrigenCompra.Manual,
            NumeroFactura = "001-002-000000123",
            Autorizacion = Clave,
            FechaEmision = new DateTime(2026, 9, 11),
            FechaRegistro = new DateTime(2026, 9, 12),
            Proveedor = prov,
            Detalles = detalles,
            Impuestos = impuestos,
            Propina = propina,
            FormaPagoId = formaPago,
            Retenciones = CalculadorRetenciones.Calcular(detalles, formaPago, prov.CuentaGasto, Catalogo),
            NumeroRetencion = "001-001-000025829",
            NumeroOc = numeroOc,
        };
    }

    private static string Resumen(LineaOc l) => FormattableString.Invariant(
        $"{l.Tipo}|{l.ItemId}|{l.Descripcion}|{l.Cantidad:0.####}|{l.Monto:0.00}|{l.CuentaId}");

    [Fact]
    public void Tarjeta_de_credito_usa_items_332G_y_retencion_en_cero()
    {
        var detalles = new List<LineaDetalle> { Linea("CON IVA", 13.04m, cantidad: 4.627m), Linea("TARIFA CERO", 5m, "0", 0) };
        PreparacionCompra.AplicarNoRetencion(detalles, FormaPagoRetencion.TarjetaCredito, Catalogo);

        var r = ArmadorOc.Armar(Entrada(detalles, [Iva(13.04m, 1.96m)], FormaPagoRetencion.TarjetaCredito, propina: 1.30m), Catalogo);

        Assert.True(r.Correcto, string.Join(" / ", r.Errores));
        Assert.Equal(
            [
                "Detalle|C3|CON IVA|4.627|13.04|60505",
                "Detalle|C4|TARIFA CERO|1|5.00|60505",
                "Impuesto|15% IVA COMPRAS|IVA|13.04|1.96|14233",
                "Propina|C11|Propina / Otros|1|1.30|60505",
                "Autorizacion|AUT-SRI|" + Clave + "|0|0.00|10000",
                "Retencion|0% 332G - Sin Ret.TC|DESC 0% 332G - Sin Ret.TC|18.04|0.00|60000",
                "Relleno||.|0|0.00|",
                "Relleno||.|0|0.00|",
            ],
            r.Oc!.Lineas.Select(Resumen));
        Assert.Equal(13.04m / 4.627m, r.Oc.Lineas[0].PrecioUnitario);
        Assert.Equal(1.96m / 13.04m, r.Oc.Lineas[2].PrecioUnitario);
        Assert.Equal(("FACTURA", "001-002-000000123", "001-001-000025829", "01", "Manual"),
            (r.Oc.ShipVia, r.Oc.NumeroFactura, r.Oc.NumeroRetencion, r.Oc.EstadoSustento, r.Oc.Zip));
    }

    [Fact]
    public void Con_retencion_usa_items_RF_SI_y_calcula_renta_e_iva()
    {
        var detalles = new List<LineaDetalle>
        {
            Linea("A", 50m, rf: "3% OTROS-344", riva: "70% RET IVA-729"),
            Linea("B", 36.96m, rf: "3% OTROS-344", riva: "70% RET IVA-729"),
            Linea("C", 7m, "0", 0, rf: "3% OTROS-344"),
        };

        var r = ArmadorOc.Armar(Entrada(detalles, [Iva(86.96m, 13.04m)], FormaPagoRetencion.Otros), Catalogo);

        Assert.True(r.Correcto, string.Join(" / ", r.Errores));
        var l = r.Oc!.Lineas;
        Assert.Equal(["C1", "C1", "C15"], l.Where(x => x.Tipo == TipoLineaOc.Detalle).Select(x => x.ItemId));
        var renta = l.Single(x => x.ItemId == "3% OTROS-344");
        Assert.Equal((93.96m, -2.82m, "24050"), (renta.Cantidad, renta.Monto, renta.CuentaId));
        Assert.Equal(-2.82m / 93.96m, renta.PrecioUnitario); // el redondeo cambió el monto: precio = monto / base
        var iva = l.Single(x => x.ItemId == "70% RET IVA-729");
        Assert.Equal((13.04m, -9.13m), (iva.Cantidad, iva.Monto)); // base = 86.96 × 15 % = 13.044 → 13.04
    }

    [Fact]
    public void Retencion_asumida_agrega_la_contrapartida_sin_item_a_la_cuenta_de_gasto()
    {
        var detalles = new List<LineaDetalle> { Linea("A", 80m, rf: "3% OTROS-344", riva: "70% RET IVA-729") };

        var r = ArmadorOc.Armar(Entrada(detalles, [Iva(80m, 12m)], FormaPagoRetencion.RetencionAsumida), Catalogo);

        Assert.True(r.Correcto, string.Join(" / ", r.Errores));
        var asumidas = r.Oc!.Lineas.Where(x => x.Tipo == TipoLineaOc.RetencionAsumida).ToList();
        Assert.Equal(
            ["RetencionAsumida||DESC 3% OTROS-344|80|2.40|60505", "RetencionAsumida||DESC 70% RET IVA-729|12|8.40|60505"],
            asumidas.Select(Resumen));
        Assert.Equal(0.03m, asumidas[0].PrecioUnitario);
    }

    [Fact]
    public void Seguros_322_reparten_10_por_ciento_con_retencion_y_90_sin()
    {
        var detalles = new List<LineaDetalle> { Linea("POLIZA", 100m, rf: "2% SEGUROS-322"), Linea("GASTO", 50m) };

        var entrada = Entrada(detalles, [Iva(150m, 22.5m)], FormaPagoRetencion.Otros);
        var r = ArmadorOc.Armar(entrada, Catalogo);

        Assert.True(r.Correcto, string.Join(" / ", r.Errores));
        Assert.Equal(
            ["Detalle|C1|POLIZA|1|10.00|60505", "Detalle|C8|GASTO|1|50.00|60505", "Detalle|C8|POLIZA|1|90.00|60505"],
            r.Oc!.Lineas.Where(x => x.Tipo == TipoLineaOc.Detalle).Select(Resumen));
        var ret = r.Oc.Lineas.Single(x => x.Tipo == TipoLineaOc.Retencion);
        Assert.Equal((10m, -0.20m), (ret.Cantidad, ret.Monto));
        Assert.Equal(100m, entrada.Detalles[0].MontoSinImpuestos); // no toca lo que ve el digitador
    }

    [Fact]
    public void Nota_de_venta_no_admite_retenciones()
    {
        var detalles = new List<LineaDetalle> { Linea("A", 10m, rf: "3% OTROS-344"), Linea("B", 10m, rf: "3% OTROS-344") };

        var r = ArmadorOc.Armar(Entrada(detalles, [Iva(20m, 3m)], FormaPagoRetencion.Otros, tipo: TipoDocumentoCompra.NotaDeVenta), Catalogo);

        Assert.Null(r.Oc);
        Assert.Single(r.Errores, e => e.Contains("NOTA DE VENTA\" NO APLICA RETENCIONES"));
    }

    [Fact]
    public void Con_retencion_cada_linea_debe_llevarla_y_el_proveedor_email()
    {
        var detalles = new List<LineaDetalle> { Linea("SIN RET", 10m) };

        var r = ArmadorOc.Armar(Entrada(detalles, [Iva(10m, 1.5m)], FormaPagoRetencion.Otros, proveedor: CatalogoPrueba.Proveedor(email: "")), Catalogo);

        Assert.Contains("Ver detalle SIN RET, según la forma de pago seleccionada debería aplicar retenciones", r.Errores);
        Assert.Contains("Revise la información del PROVEEDOR: email del proveedor es requerido", r.Errores);
        Assert.Contains("Debe especificar los detalles de retención", r.Errores);
    }

    [Fact]
    public void Sin_retencion_rechaza_una_retencion_distinta_de_332()
    {
        var detalles = new List<LineaDetalle> { Linea("A", 10m, rf: "3% OTROS-344") };

        var r = ArmadorOc.Armar(Entrada(detalles, [Iva(10m, 1.5m)], FormaPagoRetencion.TarjetaCredito), Catalogo);

        Assert.Contains("Ver detalle A, según la forma de pago seleccionada no debería aplicar retenciones", r.Errores);
    }

    [Theory]
    [InlineData(TipoDocumentoCompra.Liquidacion, "04", "PROVEEDOR con RUC: No puede ingresar una liquidación en compra.")]
    [InlineData(TipoDocumentoCompra.Factura, "05", "El proveedor con cédula se usa para Liquidación en compra; solo en reposición de caja chica.")]
    [InlineData(TipoDocumentoCompra.Factura, "08", "PROVEEDOR del exterior: Debe ingresar liquidación en compra.")]
    public void Valida_tipo_de_documento_contra_tipo_de_identificacion(TipoDocumentoCompra tipo, string tipoId, string error)
    {
        var detalles = new List<LineaDetalle> { Linea("A", 10m) };
        PreparacionCompra.AplicarNoRetencion(detalles, FormaPagoRetencion.TarjetaCredito, Catalogo);

        var r = ArmadorOc.Armar(Entrada(detalles, [Iva(10m, 1.5m)], FormaPagoRetencion.TarjetaCredito, tipo: tipo,
            proveedor: CatalogoPrueba.Proveedor(tipo: tipoId)), Catalogo);

        Assert.Contains(error, r.Errores);
    }

    [Fact]
    public void Proveedor_del_exterior_requiere_los_datos_de_pago()
    {
        var sinDatos = CatalogoPrueba.Proveedor(tipo: "08");
        Assert.Contains("Tipo de identificación es REQUERIDO", ArmadorOc.ErrorProveedorExterior(sinDatos));

        var completo = sinDatos with { CustomField1 = """["02","02","","","593","NO",""]""" };
        Assert.Null(ArmadorOc.ErrorProveedorExterior(completo));
        Assert.Null(ArmadorOc.ErrorProveedorExterior(CatalogoPrueba.Proveedor()));
    }

    [Theory]
    [InlineData("OC-8757", true)]
    [InlineData("OC-10000", true)] // Corrección C4: el `.exe` exigía 7 caracteres
    [InlineData("OC-12", false)]
    [InlineData("OC8757", false)]
    public void Numero_de_oc(string numero, bool valido)
    {
        var detalles = new List<LineaDetalle> { Linea("A", 10m) };
        PreparacionCompra.AplicarNoRetencion(detalles, FormaPagoRetencion.TarjetaCredito, Catalogo);

        var r = ArmadorOc.Armar(Entrada(detalles, [Iva(10m, 1.5m)], FormaPagoRetencion.TarjetaCredito, numeroOc: numero), Catalogo);

        Assert.Equal(valido, !r.Errores.Contains("Ingrese un número de Orden de Compra correcto xx-xxxx"));
    }

    [Fact]
    public void Desde_xml_advierte_si_la_suma_no_cuadra_con_el_total_de_la_factura()
    {
        var factura = LectorFacturaSri.Leer(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "factura-sintetica.xml"))).Factura!;
        var detalles = PreparacionCompra.Detalles(factura, "60505", [], Catalogo);
        PreparacionCompra.AplicarNoRetencion(detalles, FormaPagoRetencion.TarjetaCredito, Catalogo);
        var (impuestos, _) = PreparacionCompra.Impuestos(factura, "60505", Catalogo);
        EntradaCompra Con(IReadOnlyList<LineaDetalle> d) => new()
        {
            RucEmpresa = "1799999998001", TipoDocumento = TipoDocumentoCompra.Factura, Sustento = SustentoCompra.Credito,
            Origen = OrigenCompra.Propia, Factura = factura, NumeroFactura = factura.NumeroCompleto, Autorizacion = factura.ClaveAcceso,
            FechaEmision = factura.FechaEmision, FechaRegistro = factura.FechaEmision, Proveedor = CatalogoPrueba.Proveedor(),
            Detalles = d, Impuestos = impuestos, Propina = factura.Totales.Propina, FormaPagoId = FormaPagoRetencion.TarjetaCredito,
            Retenciones = CalculadorRetenciones.Calcular(d, FormaPagoRetencion.TarjetaCredito, "60505", Catalogo),
            NumeroRetencion = "001-001-000000001", NumeroOc = "OC-0001",
        };

        var cuadra = ArmadorOc.Armar(Con(detalles), Catalogo);
        detalles[0].MontoSinImpuestos = 90m; // el digitador cambia un monto a mano
        var noCuadra = ArmadorOc.Armar(Con(detalles), Catalogo);

        Assert.True(cuadra.Correcto, string.Join(" / ", cuadra.Errores));
        Assert.DoesNotContain(cuadra.Avisos, a => a.StartsWith("La suma de la compra"));
        Assert.True(noCuadra.Correcto); // advierte, no bloquea
        Assert.Single(noCuadra.Avisos, a => a.StartsWith("La suma de la compra"));
    }

    [Theory]
    [InlineData("001-001-000000001", true)]
    [InlineData("001-001-1", true)]
    [InlineData("001-001-000000000", false)]
    [InlineData("001001000000001", false)]
    [InlineData("001-001-0000000001", false)]
    public void Numero_de_factura_digitada(string numero, bool valido) => Assert.Equal(valido, ArmadorOc.EsNumeroFacturaValido(numero));
}

public class PreparacionCompraTests
{
    private static readonly CatalogoCompras Catalogo = CatalogoPrueba.Crear();
    private static readonly FacturaRecibida Factura =
        LectorFacturaSri.Leer(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "factura-sintetica.xml"))).Factura!;

    [Fact]
    public void Detalles_descartan_total_cero_y_toman_la_cuenta_de_gasto()
    {
        var d = PreparacionCompra.Detalles(Factura, "60505", [], Catalogo);

        Assert.Equal(["SERVICIO CON IVA", "BIEN TARIFA CERO"], d.Select(x => x.Descripcion));
        Assert.All(d, x => Assert.Equal("60505", x.CuentaId));
        Assert.Equal(("4", 15m), (d[0].CodigoPorcentajeIva, d[0].TarifaIva));
    }

    [Fact]
    public void Detalles_usan_el_item_aprendido_del_proveedor_si_existe_en_sage()
    {
        ConfiguracionItemProveedor[] configs =
        [
            new(1, "1799999998001", "1799999999001", "A-1", "MERC-1"),
            new(2, "1799999998001", "1799999999001", "B-2", "NO-EXISTE"),
        ];

        var d = PreparacionCompra.Detalles(Factura, "60505", configs, Catalogo);

        Assert.Equal(("MERC-1", "14100"), (d[0].ItemId, d[0].CuentaId));
        Assert.Equal((null, "60505"), (d[1].ItemId, d[1].CuentaId));
    }

    [Fact]
    public void Resumir_agrupa_por_iva_con_codigo_gruoped()
    {
        var d = PreparacionCompra.Detalles(Factura, "60505", [], Catalogo);
        d.Add(new LineaDetalle { Descripcion = "OTRO", Cantidad = 2, MontoSinImpuestos = 20.005m, CodigoIva = "2", CodigoPorcentajeIva = "4", TarifaIva = 15 });

        var r = PreparacionCompra.Resumir(d, "60505");

        Assert.Equal([("Gruoped000", 120.00m), ("Gruoped001", 10.50m)], r.Select(x => (x.CodigoPrincipal, x.MontoSinImpuestos)));
        Assert.All(r, x => Assert.Equal((1m, "", "60505"), (x.Cantidad, x.Descripcion, x.CuentaId)));
    }

    [Theory]
    [InlineData("19", FormaPagoRetencion.TarjetaCredito)]
    [InlineData("20", FormaPagoRetencion.Otros)]
    [InlineData("01", FormaPagoRetencion.Otros)] // efectivo no está en AboutApplyTwh
    [InlineData(null, FormaPagoRetencion.Otros)]
    public void Forma_de_pago_sugerida(string? formaSri, int esperada)
    {
        var f = Factura with { Pagos = formaSri is null ? null : [new PagoFactura(formaSri, 1m)] };

        Assert.Equal(esperada, PreparacionCompra.FormaPagoSugerida(f, Catalogo));
    }

    [Fact]
    public void Impuestos_del_xml_eligen_el_item_de_iva_por_tarifa_exacta()
    {
        var (impuestos, errores) = PreparacionCompra.Impuestos(Factura, "60505", Catalogo);

        Assert.Empty(errores);
        var iva = Assert.Single(impuestos);
        Assert.Equal(("15% IVA COMPRAS", "14233", 100m, 15m), (iva.ItemId, iva.CuentaId, iva.BaseImponible, iva.Valor));
    }

    [Fact]
    public void Impuestos_del_xml_sin_item_para_la_tarifa_es_error()
    {
        // Corrección C2: el `.exe` usaba el primer ítem IMPUESTO (el de 15 %) para una factura al 5 %.
        var f = Factura with { Totales = Factura.Totales with { Impuestos = [new ImpuestoTotal("2", "5", 100m, 5m, 5m)] } };

        var (impuestos, errores) = PreparacionCompra.Impuestos(f, "60505", Catalogo);

        Assert.Null(Assert.Single(impuestos).ItemId);
        Assert.Contains("No hay un ítem de IVA (5%) activo en Sage: revise la categoría IMPUESTO.", errores);
    }

    [Fact]
    public void Impuestos_digitados_agrupan_por_tarifa()
    {
        var d = new List<LineaDetalle>
        {
            new() { MontoSinImpuestos = 10.10m, CodigoPorcentajeIva = "4" },
            new() { MontoSinImpuestos = 0.00m, CodigoPorcentajeIva = "4" },
            new() { MontoSinImpuestos = 5m, CodigoPorcentajeIva = "0" },
        };

        var (impuestos, errores) = PreparacionCompra.ImpuestosDigitados(d, Catalogo);

        Assert.Empty(errores);
        var iva = Assert.Single(impuestos);
        Assert.Equal((10.10m, 1.52m, "15% IVA COMPRAS"), (iva.BaseImponible, iva.Valor, iva.ItemId)); // 1.515 → 1.52
    }
}
