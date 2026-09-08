using PsaWeb.Comprobantes.Clientes;
using PsaWeb.Comprobantes.Retenciones;
using PsaWeb.Comprobantes.Venta;
using PsaWeb.Datil.Model;

namespace PsaWeb.Comprobantes.Tests;

public class ConstructorFacturaTests
{
    private static readonly EmpresaEmisora Emisor = new(
        "1791313747001", "SANCEV ELECTRICA INDUSTRIAL CIA. LTDA.", "SANCEV",
        "CHIRIBOGA N50-40", "", true);

    private static readonly EstablecimientoInfo Establecimiento = new(35, "001", "003", "CHIRIBOGA N50-40");

    private static ClienteSri Cliente() => new()
    {
        Identificacion = "1790011110001",
        TipoIdentificacion = "04",
        RazonSocial = "CLIENTE S.A.",
        Direccion = "Quito",
        Email = "cliente@ejemplo.com",
        Telefono = "022345678",
        Errores = Array.Empty<string>(),
    };

    private static FacturaVentaLinea LineaIva(double subtotal = 100, double pct = 0.12) => new()
    {
        Descripcion = "Servicio",
        Cantidad = 1,
        PrecioUnitario = subtotal,
        SubtotalSinImpuestos = subtotal,
        BaseImponibleIva = subtotal,
        IvaValor = Math.Round(subtotal * pct, 2),
        IvaPorcentaje = pct,
        CodigoPorcentajeIva = "2",
        CodigoPrincipal = "ART-1",
    };

    private static FacturaVentaLeida Leida(
        DateTime? vence = null, IEnumerable<FacturaVentaLinea>? lineas = null,
        ClienteSri? cliente = null, IEnumerable<InfoAdicionalItem>? info = null)
    {
        var ls = (lineas ?? new[] { LineaIva() }).ToList();
        return new FacturaVentaLeida
        {
            PostOrderPeach = "9001",
            Cliente = cliente ?? Cliente(),
            Lineas = ls,
            InfoAdicional = (info ?? Array.Empty<InfoAdicionalItem>()).ToList(),
            Cabecera = new FacturaVentaCabecera
            {
                NumeroCompleto = "001-003-000013538",
                Secuencial = "000013538",
                CodigoEstablecimiento = "001",
                PuntoEmision = "003",
                CustomerRecordNumber = "42",
                FechaEmision = new DateTime(2026, 9, 4),
                FechaVencimiento = vence,
                TotalConImpuestos = ls.Sum(l => l.SubtotalSinImpuestos + l.IvaValor),
                IvaValor = ls.Sum(l => l.IvaValor),
                TotalSinImpuestos = ls.Sum(l => l.SubtotalSinImpuestos),
                BaseImponibleIva = ls.Where(l => l.IvaPorcentaje > 0).Sum(l => l.BaseImponibleIva),
                DescuentoTotal = 0,
                CodigoPorcentajeIva = "2",
                CodigoIva = "2",
            },
        };
    }

    private static ResultadoFactura Construir(
        FacturaVentaLeida? leida = null, short ambiente = 2, string? emailPruebas = null)
        => ConstructorFactura.Construir(Emisor, Establecimiento, leida ?? Leida(), ambiente, emailPruebas, "20");

    [Fact]
    public void Mapea_emisor_comprador_establecimiento()
    {
        var r = Construir();

        Assert.True(r.Ok, string.Join(" | ", r.Errores));
        var f = r.Factura!;
        Assert.Equal("13538", f.Secuencial); // sin ceros a la izquierda
        Assert.Equal(2, f.Ambiente);
        Assert.Equal("1791313747001", f.Emisor.Ruc);
        Assert.Equal("001", f.Emisor.Establecimiento.Codigo);
        Assert.Equal("003", f.Emisor.Establecimiento.PuntoEmision); // el del número
        Assert.Equal("1790011110001", f.Comprador.Identificacion);
        Assert.Equal("cliente@ejemplo.com", f.Comprador.Email);
        Assert.Equal("022345678", f.Comprador.Telefono);
        Assert.Equal("2026-09-04T00:00:00-05:00", f.FechaEmision.ToString("yyyy-MM-ddTHH:mm:sszzz"));
    }

    [Fact]
    public void Item_lleva_impuesto_con_tarifa_0_a_100()
    {
        var f = Construir().Factura!;
        var imp = Assert.Single(f.Items[0].Impuestos);
        Assert.Equal("2", imp.Codigo);
        Assert.Equal("2", imp.CodigoPorcentaje);
        Assert.Equal(100, imp.BaseImponible);
        Assert.Equal(12, imp.Valor);
        Assert.Equal(12, imp.Tarifa); // 0.12 -> 12
    }

    [Fact]
    public void Totales_agrupan_impuestos_por_codigo_y_porcentaje()
    {
        var r = Construir(Leida(lineas: new[]
        {
            LineaIva(subtotal: 100),
            LineaIva(subtotal: 50),
            new FacturaVentaLinea // línea sin IVA
            {
                Descripcion = "Exento", Cantidad = 1, PrecioUnitario = 20,
                SubtotalSinImpuestos = 20, BaseImponibleIva = 20, IvaPorcentaje = 0,
                CodigoPorcentajeIva = "0", CodigoPrincipal = "0",
            },
        }));

        var totales = r.Factura!.Totales;
        Assert.Equal(2, totales.Impuestos.Count);
        var conIva = totales.Impuestos.Single(i => i.CodigoPorcentaje == "2");
        Assert.Equal(150, conIva.BaseImponible);
        Assert.Equal(18, conIva.Valor); // 150 * 0.12
        Assert.Null(conIva.Tarifa); // los impuestos de totales no llevan tarifa
        var sinIva = totales.Impuestos.Single(i => i.CodigoPorcentaje == "0");
        Assert.Equal(20, sinIva.BaseImponible);
        Assert.Equal(0, sinIva.Valor);
    }

    [Fact]
    public void En_pruebas_redirige_el_email_a_la_casilla_de_pruebas()
    {
        var r = Construir(ambiente: 1, emailPruebas: "pruebas@paredes.com.ec");
        Assert.Equal("pruebas@paredes.com.ec", r.Factura!.Comprador.Email);

        // en producción NO redirige aunque haya casilla
        var prod = Construir(ambiente: 2, emailPruebas: "pruebas@paredes.com.ec");
        Assert.Equal("cliente@ejemplo.com", prod.Factura!.Comprador.Email);
    }

    [Fact]
    public void Sin_fecha_de_vencimiento_es_pago_al_contado()
    {
        var f = Construir(Leida(vence: null)).Factura!;
        Assert.Null(f.Credito);
        var pago = Assert.Single(f.Pagos);
        Assert.Equal("20", pago.Medio);
        Assert.Equal(112, pago.Total);
    }

    [Fact]
    public void Vencimiento_a_futuro_es_a_credito()
    {
        var f = Construir(Leida(vence: new DateTime(2026, 10, 4))).Factura!;
        Assert.Empty(f.Pagos);
        Assert.NotNull(f.Credito);
        Assert.Equal("2026-10-04", f.Credito!.FechaVencimiento);
        Assert.Equal(112, f.Credito.Monto);
    }

    [Fact]
    public void Vencimiento_mismo_dia_es_contado()
    {
        var f = Construir(Leida(vence: new DateTime(2026, 9, 4))).Factura!;
        Assert.Single(f.Pagos);
        Assert.Null(f.Credito);
    }

    [Fact]
    public void Guardar_lleva_los_datos_para_persistir()
    {
        var g = Construir().Guardar!;
        Assert.Equal("01", g.CodDoc);
        Assert.Equal("001-003-000013538", g.NumeroCompleto);
        Assert.Equal("000013538", g.Secuencial); // acá SÍ con ceros (columna FacturaNumber)
        Assert.Equal(35, g.EstablecimientoId);
        Assert.Equal("9001", g.PostOrderPeach);
        Assert.Equal(112, g.TotalConImpuestos);
        Assert.Equal("1790011110001", g.Persona.Identificacion);
        Assert.Null(g.Persona.Fax);
        var l = Assert.Single(g.Lineas);
        Assert.Equal("ART-1", l.CodigoPrincipal);
        Assert.Null(l.CodigoAuxiliar);
        Assert.Equal(0.12, l.IvaPorcentaje);
    }

    [Fact]
    public void Info_adicional_se_pasa_a_la_factura()
    {
        var r = Construir(Leida(info: new[]
        {
            new InfoAdicionalItem { Nombre = "Orden", Valor = "OC-1" },
        }));
        Assert.NotNull(r.Factura!.InfoAdicional);
        Assert.Equal("OC-1", r.Factura.InfoAdicional![0].Valor);
    }

    [Fact]
    public void Propaga_errores_del_lector_y_del_cliente()
    {
        var leida = Leida(cliente: new ClienteSri { Errores = new[] { "Email del cliente no válido: x" } });
        var conErrorLector = new FacturaVentaLeida
        {
            PostOrderPeach = leida.PostOrderPeach,
            Cabecera = leida.Cabecera,
            Cliente = leida.Cliente,
            Lineas = leida.Lineas,
            Errores = new[] { "No se pudo calcular correctamente el descuento." },
        };

        var r = ConstructorFactura.Construir(Emisor, Establecimiento, conErrorLector, 2, null, "20");

        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("descuento"));
        Assert.Contains(r.Errores, e => e.Contains("Email del cliente"));
    }
}
