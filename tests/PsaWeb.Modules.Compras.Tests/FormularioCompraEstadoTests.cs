using PsaWeb.Compras.Armado;
using PsaWeb.Compras.Catalogo;
using PsaWeb.Compras.Sage;
using PsaWeb.Modules.Compras.Servicios;
using PsaWeb.Seguridad;

namespace PsaWeb.Modules.Compras.Tests;

public class FormularioCompraEstadoTests
{
    private static ItemSage C(string id, string rf, string iva, string cf4 = "", string cf5 = "") =>
        new(id, id, 0, "", "COMPRA", rf, iva, cf4, cf5, "60000", false);

    private static readonly CatalogoCompras Catalogo = LectorCatalogoCompras.Armar(
        [
            C("C1", "RF: SI", "IVA: SI"), C("C11", "RF: NO", "IVA: NO", "NoGraIVA", "332"), C("C15", "RF: SI", "IVA: NO", "Imponible 0%"),
            C("C3", "RF: NO", "IVA: SI", "", "332G"), C("C4", "RF: NO", "IVA: NO", "Imponible 0%", "332G"), C("C8", "RF: NO", "IVA: SI", "", "332"),
            new("15% IVA COMPRAS", "15% IVA COMPRAS", 0, "IMPUESTO", "IVA", "15%", "15%", "", "", "14233", false),
            new("AUT-SRI", "AUT", 0, "IMPUESTO", "", "", "", "", "", "60000", false),
            new("0% 332G - Sin Ret.TC", "332G", 0, "R-IRF", "332G", "0%", "", "", "", "60000", false),
            new("3% OTROS-344", "3%", 0, "R-IRF", "3440", "-3%", "", "", "", "24050", false),
        ],
        "10000",
        new CatalogoPeachEbills(
            [new(1, "Tarjeta de crédito", "tarjeta_credito", false), new(5, "Otros", "otros", true), new(7, "Retención asumida", null, true)],
            [new("20", "otros"), new("19", "tarjeta_credito")],
            [new("0", "0%", 0m), new("4", "15%", 0.15m)],
            new Dictionary<string, string> { ["2"] = "IVA" }));

    private static ProveedorSage Existente() => new("PROV", "12", "04", "PROVEEDOR", "", "", "X", "", "", "DIR", "", "1799999999001",
        "a@b.test", "022", "SC-CE#1234#", "60505", false);

    [Fact]
    public void Nueva_nota_de_venta_va_a_costo_y_forma_de_pago_otros()
    {
        var f = FormularioCompraEstado.Nueva(TipoDocumentoCompra.NotaDeVenta);
        Assert.Equal((SustentoCompra.Costo, OrigenCompra.Manual, (int?)FormaPagoRetencion.Otros), (f.Sustento, f.Origen, f.FormaPagoId));
        Assert.Equal(SustentoCompra.Credito, FormularioCompraEstado.Nueva(TipoDocumentoCompra.Factura).Sustento);
    }

    [Fact]
    public void Lineas_calculan_subtotal_e_impuestos_digitados()
    {
        var f = FormularioCompraEstado.Nueva(TipoDocumentoCompra.Factura);
        f.AgregarLinea("60505");
        var l = f.Detalles[0];
        l.Cantidad = 3;
        l.PrecioUnitario = 3.37m;
        f.Recalcular(l);

        var imp = Assert.Single(f.Impuestos(Catalogo, out var errores));
        Assert.Empty(errores);
        Assert.Equal((10.11m, 10.11m, 1.52m), (l.MontoSinImpuestos, imp.BaseImponible, imp.Valor));

        f.CambiarIva(l, "0", Catalogo);
        Assert.Empty(f.Impuestos(Catalogo, out _));
    }

    [Fact]
    public void Forma_de_pago_sin_retencion_pone_332_en_todas_las_lineas_y_con_retencion_las_limpia()
    {
        var f = FormularioCompraEstado.Nueva(TipoDocumentoCompra.Factura);
        f.AgregarLinea("60505");
        f.AgregarLinea("60505");
        f.Detalles[0].RetencionIvaId = "70%";

        f.CambiarFormaPago(FormaPagoRetencion.TarjetaCredito, Catalogo);
        Assert.All(f.Detalles, d => Assert.Equal(("0% 332G - Sin Ret.TC", (string?)null), (d.RetencionFuenteId, d.RetencionIvaId)));

        f.CambiarFormaPago(FormaPagoRetencion.Otros, Catalogo);
        Assert.All(f.Detalles, d => Assert.Null(d.RetencionFuenteId));
    }

    [Fact]
    public void Impuestos_originales_se_usan_hasta_que_se_tocan_las_lineas()
    {
        var f = FormularioCompraEstado.Nueva(TipoDocumentoCompra.Factura);
        f.AgregarLinea("60505");
        f.Detalles[0].MontoSinImpuestos = 100;
        f.LineasModificadas = false;
        f.ImpuestosOriginales = [new LineaImpuesto { Codigo = "2", BaseImponible = 100, Valor = 14.99m, ItemId = "15% IVA COMPRAS", CuentaId = "14233" }];

        Assert.Equal(14.99m, f.Impuestos(Catalogo, out _).Single().Valor);
        f.Recalcular(f.Detalles[0]);
        Assert.NotEqual(14.99m, f.Impuestos(Catalogo, out _).Single().Valor);
    }

    [Fact]
    public void Entrada_arma_una_oc_valida_con_numeros_previstos()
    {
        var f = FormularioCompraEstado.Nueva(TipoDocumentoCompra.Factura);
        f.Proveedor = ProveedorEdicion.Desde(Existente());
        f.NumeroFactura = "001-001-000000123";
        f.Autorizacion = "1234567890";
        f.NumeroOcPrevisto = "OC-8927";
        f.NumeroRetencionPrevisto = "001-001-000026000";
        f.AgregarLinea("60505");
        f.Detalles[0].Descripcion = "SERVICIO";
        f.Detalles[0].PrecioUnitario = 100;
        f.Recalcular(f.Detalles[0]);
        f.Detalles[0].RetencionFuenteId = "3% OTROS-344";

        var r = ArmadorOc.Armar(f.Entrada("1799999998001", Catalogo)!, Catalogo);

        Assert.True(r.Correcto, string.Join(" / ", r.Errores));
        Assert.Equal(("OC-8927", "001-001-000026000", "Manual"), (r.Oc!.Referencia, r.Oc.NumeroRetencion, r.Oc.Zip));
        Assert.Equal(["C1", "15% IVA COMPRAS", "AUT-SRI", "3% OTROS-344", null, null], r.Oc.Lineas.Select(x => x.ItemId));
        Assert.True(f.NumeroOcAutomatico);
        f.NumeroOcManual = "OC-9000";
        Assert.False(f.NumeroOcAutomatico);
    }

    [Fact]
    public void Proveedor_existente_conserva_lo_que_no_se_edita_y_el_nuevo_sigue_las_convenciones()
    {
        var e = ProveedorEdicion.Desde(Existente());
        Assert.Equal(("1234", false), (e.ContribuyenteEspecial, e.ObligadoContabilidad));
        e.Email = "nuevo@b.test";
        e.ObligadoContabilidad = true;
        var p = e.AProveedorSage();
        Assert.Equal(("X", "60505", "nuevo@b.test", "OC-CE#1234#", "12"), (p.CustomField2, p.CuentaGasto, p.Email, p.Telefono2, p.RecordNumber));

        var n = ProveedorEdicion.Nuevo("AB123", "EXTERIOR SA");
        n.Id = "EXTERIOR SA";
        n.CuentaGasto = "60505";
        var pn = n.AProveedorSage();
        Assert.Equal(("06", "AB123", "AB123", (string?)null), (pn.TipoIdentificacion, pn.Pais, pn.CustomField4, pn.RecordNumber));
    }

    [Fact]
    public void Desde_la_factura_del_sri_precarga_todo_y_resume()
    {
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "factura-sintetica.xml"));
        var factura = PsaWeb.Compras.Sri.LectorFacturaSri.Leer(xml).Factura!;
        var proveedor = ProveedorEdicion.NuevoDesdeFactura(factura, "PROVEEDOR DE PRUEBA");
        proveedor.CuentaGasto = "60505";

        var f = FormularioCompraEstado.DesdeFactura(factura, "1799999998001", proveedor, [], Catalogo, out var errores);

        Assert.Empty(errores);
        Assert.Equal(("001-002-000000123", OrigenCompra.Propia, true, (int?)FormaPagoRetencion.TarjetaCredito, 5.00m),
            (f.NumeroFactura, f.Origen, f.ImpuestosFijos, f.FormaPagoId, f.Propina));
        Assert.Equal(2, f.Detalles.Count);
        Assert.All(f.Detalles, d => Assert.Equal("0% 332G - Sin Ret.TC", d.RetencionFuenteId)); // pago 19 → tarjeta → 332G
        Assert.Equal(("OC-", "PROVEEDOR DE PRUEBA S.A."), (proveedor.AProveedorSage().Telefono2[..3], proveedor.NombreCompleto));

        f.AlternarResumen(Catalogo);
        Assert.True(f.Resumido);
        Assert.Equal(["Gruoped000", "Gruoped001"], f.Detalles.Select(d => d.CodigoPrincipal));
        Assert.All(f.Detalles, d => Assert.Equal("0% 332G - Sin Ret.TC", d.RetencionFuenteId));
        f.AlternarResumen(Catalogo);
        Assert.Equal(["A-1", "B-2"], f.Detalles.Select(d => d.CodigoPrincipal));

        // Desde el XML los impuestos no se recalculan aunque se toquen las líneas.
        f.Recalcular(f.Detalles[0]);
        Assert.Equal(15.00m, f.Impuestos(Catalogo, out _).Single().Valor);
    }

    [Fact]
    public void Desde_texto_toma_solo_claves_de_49_digitos()
    {
        var clave = "0101202601179999999900120010020000001231234567811";
        var texto = $"COMPROBANTE\t{clave}\t2026\n{clave}\notra 12345 y {clave}9 (50 dígitos no)";

        Assert.Equal([clave], ServicioRecibidos.ClavesDelTexto(texto));
    }

    [Fact]
    public void Proveedor_existente_sin_editar_queda_igual()
    {
        // Nombre de 35 caracteres guardado como Name(30) + CustomField0(5): no se vuelve a partir si no se editó.
        var original = Existente() with { Nombre = "MARIA BERNANDA PAZ BORJA COMERCI", CustomField0 = "AL SA", Telefono2 = "OC-" };
        Assert.Equal(original, ProveedorEdicion.Desde(original).AProveedorSage());
    }

    [Fact]
    public void Copia_de_una_oc_limpia_factura_numeros_y_fechas()
    {
        var entrada = new EntradaCompra
        {
            RucEmpresa = "1", TipoDocumento = TipoDocumentoCompra.Factura, Sustento = SustentoCompra.Credito, Origen = OrigenCompra.Manual,
            NumeroFactura = "001-001-000000009", Autorizacion = "1234567890", FechaEmision = new DateTime(2026, 1, 1),
            FechaRegistro = new DateTime(2026, 1, 2), Proveedor = Existente(), Detalles = [new LineaDetalle { Descripcion = "X", Cantidad = 1, MontoSinImpuestos = 5 }],
            Impuestos = [], NumeroOc = "OC-0001", NumeroRetencion = "001-001-000000001", FormaPagoId = 5,
        };
        var cab = new OcGuardadaCabecera(1, "OC-0001", entrada.FechaEmision, entrada.FechaRegistro, "FACTURA", entrada.NumeroFactura,
            "001-001-000000001", "01", "Manual", "PROV", false);

        var copia = FormularioCompraEstado.DesdeOc(new ResultadoRecarga(entrada, []), cab, copia: true);
        var existente = FormularioCompraEstado.DesdeOc(new ResultadoRecarga(entrada, []), cab, copia: false);

        Assert.Equal((ModoFormulario.Copia, "", "", DateTime.Today, (string?)null, (string?)null),
            (copia.Modo, copia.NumeroFactura, copia.Autorizacion, copia.FechaEmision, copia.NumeroOcExistente, copia.NumeroRetencionExistente));
        Assert.Equal((ModoFormulario.Existente, "OC-0001", "001-001-000000001"), (existente.Modo, existente.NumeroOcExistente, existente.NumeroRetencionExistente));
        Assert.NotSame(entrada.Detalles[0], copia.Detalles[0]);
    }
}

public class PermisosComprasTests
{
    private sealed class Directorio(params string[] permisos) : ISecurityDirectory
    {
        public Task<IReadOnlyList<EmpresaDelUsuario>> EmpresasDelUsuarioAsync(string usuario, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<EmpresaDelUsuario>>([]);
        public Task<IReadOnlyDictionary<string, int>> ContarEmpresasAsync(IEnumerable<string> usuarios, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());
        public Task<IReadOnlySet<string>> PermisosAsync(string usuario, string ruc, CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(permisos.ToHashSet());
        public Task<string?> EmailUsuarioAsync(string usuario, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> EmailsPorRolAsync(string ruc, string rol = "Supervisor", CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class Proveedor(ISecurityDirectory d) : IServiceProvider
    {
        public object? GetService(Type t) => t == typeof(ISecurityDirectory) ? d : null;
    }

    private static Task<PermisosCompras> Permisos(params string[] p) =>
        new ServicioCompras(null!, null!, null!, null!, null!, new Proveedor(new Directorio(p))).PermisosAsync("u", "r");

    [Fact]
    public async Task Llaves_propias_y_provisionales()
    {
        Assert.Equal(new PermisosCompras(true, true), await Permisos(PsaWeb.Seguridad.Permisos.RegistrarCompras));
        Assert.Equal(new PermisosCompras(true, false), await Permisos(PsaWeb.Seguridad.Permisos.VerCompras));
        Assert.Equal(new PermisosCompras(false, false), await Permisos("otra"));
        // GateProvisional: las llaves de retenciones de compra (las que copia el script) también habilitan.
        Assert.Equal(new PermisosCompras(true, true), await Permisos(PsaWeb.Seguridad.Permisos.HacerRetencion));
        Assert.Equal(new PermisosCompras(true, false), await Permisos(PsaWeb.Seguridad.Permisos.VerRetenciones));
    }
}
