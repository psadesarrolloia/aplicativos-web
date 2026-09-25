using PsaWeb.Compras.Armado;
using PsaWeb.Compras.Catalogo;
using PsaWeb.Compras.Sage;
using PsaWeb.Compras.Sri;

namespace PsaWeb.Modules.Compras.Servicios;

/// <summary>Cómo se abrió el formulario (<c>Bill.LoadNewBill</c> / <c>LoadInfoBill(DataRow)</c> / <c>LoadCopyBill</c>).</summary>
public enum ModoFormulario { Nueva, Existente, Copia }

/// <summary>Proveedor tal como lo edita el digitador (port del panel <c>ContactForm</c>).</summary>
public sealed class ProveedorEdicion
{
    /// <summary>Proveedor de Sage cuando ya existe; <c>null</c> = nuevo.</summary>
    public ProveedorSage? Existente { get; set; }

    public bool EsNuevo => Existente is null;
    public string Id { get; set; } = string.Empty;
    public string Identificacion { get; set; } = string.Empty;
    public string TipoIdentificacion { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string CuentaGasto { get; set; } = string.Empty;
    public string ContribuyenteEspecial { get; set; } = string.Empty;
    public bool ObligadoContabilidad { get; set; }

    /// <summary>JSON de 7 valores del proveedor del exterior (<c>CustomField1</c>).</summary>
    public string DatosExterior { get; set; } = string.Empty;

    public static ProveedorEdicion Desde(ProveedorSage p) => new()
    {
        Existente = p,
        Id = p.Id,
        Identificacion = p.Identificacion,
        TipoIdentificacion = p.TipoIdentificacion,
        NombreCompleto = p.NombreCompleto,
        Direccion = p.Direccion1 + p.Direccion2,
        Email = p.Email,
        Telefono = p.Telefono,
        CuentaGasto = p.CuentaGasto,
        ContribuyenteEspecial = ContribuyenteDe(p.Telefono2),
        ObligadoContabilidad = p.ObligadoContabilidad,
        DatosExterior = p.CustomField1,
    };

    /// <summary>Proveedor nuevo con la identificación escrita (el tipo se deduce como en <c>sageVendor.SriPersonId</c>).</summary>
    public static ProveedorEdicion Nuevo(string identificacion, string? razonSocial = null, string? direccion = null) => new()
    {
        Identificacion = identificacion.Trim(),
        TipoIdentificacion = ReglasProveedor.Identificacion(identificacion.Trim()).Tipo,
        NombreCompleto = razonSocial ?? string.Empty,
        Direccion = direccion ?? string.Empty,
    };

    /// <summary>Proveedor nuevo con los datos del emisor de la factura (<c>LoadInfoBill</c>: nombre, dirección, CE, obligado).</summary>
    public static ProveedorEdicion NuevoDesdeFactura(FacturaRecibida factura, string idPropuesto)
    {
        var p = Nuevo(factura.Emisor.Ruc, factura.Emisor.RazonSocial, factura.Emisor.DireccionMatriz);
        p.Id = idPropuesto;
        p.ContribuyenteEspecial = factura.Emisor.ContribuyenteEspecial ?? string.Empty;
        p.ObligadoContabilidad = factura.Emisor.ObligadoContabilidad;
        return p;
    }

    /// <summary>
    /// El proveedor como irá a Sage. Existente: se conservan los campos que el formulario no edita (CustomField2/3,
    /// cuenta de gasto: <c>LoadVendor</c> no la cambia en uno existente). Nuevo: convenciones de <c>sageVendor</c>.
    /// </summary>
    public ProveedorSage AProveedorSage()
    {
        var (nombre, cf0) = ReglasProveedor.PartirNombre(NombreCompleto.Trim());
        var (dir1, dir2) = ReglasProveedor.PartirDireccion(Direccion.Trim());
        var telefono2 = ReglasProveedor.Telefono2(ObligadoContabilidad, ContribuyenteEspecial.Trim());
        if (Existente is { } e)
        {
            // Solo se reescribe lo que el digitador cambió: volver a partir nombre/dirección o a armar PhoneNumber2 con las
            // reglas del `.exe` alteraría proveedores que Sage guarda de otra forma (F4: «Actualizado» sin haber editado nada).
            var nombreIgual = NombreCompleto.Trim() == e.NombreCompleto.Trim();
            var direccionIgual = Direccion.Trim() == (e.Direccion1 + e.Direccion2).Trim();
            var tel2Igual = ContribuyenteEspecial.Trim() == ContribuyenteDe(e.Telefono2) && ObligadoContabilidad == e.ObligadoContabilidad;
            return e with
            {
                TipoIdentificacion = TipoIdentificacion,
                Nombre = nombreIgual ? e.Nombre : nombre,
                CustomField0 = nombreIgual ? e.CustomField0 : cf0,
                CustomField1 = DatosExterior,
                Direccion1 = direccionIgual ? e.Direccion1 : dir1,
                Direccion2 = direccionIgual ? e.Direccion2 : dir2,
                Email = Email.Trim(),
                Telefono = Telefono.Trim(),
                Telefono2 = tel2Igual ? e.Telefono2 : telefono2,
            };
        }
        var identificacion = Identificacion.Trim();
        var cf4 = TipoIdentificacion is "06" or "08" ? identificacion : string.Empty;
        return new ProveedorSage(Id.Trim(), null, TipoIdentificacion, nombre, cf0, DatosExterior, string.Empty, string.Empty, cf4,
            dir1, dir2, identificacion, Email.Trim(), Telefono.Trim(), telefono2, CuentaGasto, false);
    }

    private static string ContribuyenteDe(string telefono2)
    {
        var i = telefono2.IndexOf("CE#", StringComparison.Ordinal);
        if (i < 0) return string.Empty;
        var fin = telefono2.IndexOf('#', i + 3);
        return fin < 0 ? telefono2[(i + 3)..] : telefono2[(i + 3)..fin];
    }
}

/// <summary>
/// Estado editable del formulario de compra (port de <c>Bill</c>): lo que la página muestra y el digitador cambia. Convierte a
/// <see cref="EntradaCompra"/> para validar/armar con <see cref="ArmadorOc"/>.
/// </summary>
public sealed class FormularioCompraEstado
{
    public ModoFormulario Modo { get; set; }
    public TipoDocumentoCompra Tipo { get; set; } = TipoDocumentoCompra.Factura;
    public SustentoCompra Sustento { get; set; } = SustentoCompra.Credito;
    public OrigenCompra Origen { get; set; } = OrigenCompra.Manual;
    public string NumeroFactura { get; set; } = string.Empty;
    public string Autorizacion { get; set; } = string.Empty;
    public DateTime FechaEmision { get; set; } = DateTime.Today;
    public DateTime FechaRegistro { get; set; } = DateTime.Today;
    public ProveedorEdicion? Proveedor { get; set; }
    public List<LineaDetalle> Detalles { get; } = [];
    public decimal Propina { get; set; }
    public int? FormaPagoId { get; set; }

    /// <summary>Factura del SRI cuando el formulario viene de un XML (F5).</summary>
    public FacturaRecibida? Factura { get; set; }

    /// <summary>OC existente (modo <see cref="ModoFormulario.Existente"/>): su nº y su nº de retención se conservan.</summary>
    public string? NumeroOcExistente { get; set; }
    public string? NumeroRetencionExistente { get; set; }

    /// <summary>Nº de OC / retención escritos a mano (vacío = automático).</summary>
    public string NumeroOcManual { get; set; } = string.Empty;
    public string NumeroRetencionManual { get; set; } = string.Empty;

    /// <summary>Vista previa de la numeración automática (el Bridge asigna la definitiva).</summary>
    public string NumeroOcPrevisto { get; set; } = string.Empty;
    public string NumeroRetencionPrevisto { get; set; } = string.Empty;

    /// <summary>Impuestos de la OC reabierta o del XML: se usan mientras no se toquen las líneas.</summary>
    public IReadOnlyList<LineaImpuesto>? ImpuestosOriginales { get; set; }
    public bool LineasModificadas { get; set; }

    public bool EsElectronica => Autorizacion.Trim().Length == 49;

    /// <summary>
    /// Desde el XML los impuestos son los de la factura aunque se editen las líneas (<c>UpdateTaxLines</c> solo recalcula en una
    /// compra digitada).
    /// </summary>
    public bool ImpuestosFijos { get; set; }

    /// <summary>Detalles tal como vienen del XML (para «Resumir detalles» / «Detalles originales»).</summary>
    public List<LineaDetalle>? DetallesOriginales { get; set; }
    public bool Resumido { get; private set; }

    /// <summary>
    /// Formulario precargado desde la factura del SRI (<c>Bill.LoadInfoBill(Factura)</c>): detalles con el ítem aprendido,
    /// impuestos del XML, forma de pago sugerida por el primer pago (332* automático si no aplica retención).
    /// </summary>
    public static FormularioCompraEstado DesdeFactura(FacturaRecibida factura, string rucEmpresa, ProveedorEdicion proveedor,
        IReadOnlyList<ConfiguracionItemProveedor> configuraciones, CatalogoCompras catalogo, out IReadOnlyList<string> errores)
    {
        var cuentaGasto = proveedor.CuentaGasto;
        var (impuestos, erroresImp) = PreparacionCompra.Impuestos(factura, cuentaGasto, catalogo);
        errores = erroresImp;
        var f = new FormularioCompraEstado
        {
            Modo = ModoFormulario.Nueva,
            Tipo = TipoDocumentoCompra.Factura,
            Sustento = SustentoCompra.Credito,
            Origen = PreparacionCompra.Origen(factura, rucEmpresa),
            Factura = factura,
            NumeroFactura = factura.NumeroCompleto,
            Autorizacion = factura.ClaveAcceso,
            FechaEmision = factura.FechaEmision,
            FechaRegistro = DateTime.Today,
            Proveedor = proveedor,
            Propina = factura.Totales.Propina,
            FormaPagoId = PreparacionCompra.FormaPagoSugerida(factura, catalogo),
            ImpuestosOriginales = impuestos,
            ImpuestosFijos = true,
        };
        f.Detalles.AddRange(PreparacionCompra.Detalles(factura, cuentaGasto, configuraciones, catalogo));
        f.DetallesOriginales = f.Detalles.Select(d => d.Copiar()).ToList();
        PreparacionCompra.AplicarNoRetencion(f.Detalles, f.FormaPagoId!.Value, catalogo);
        return f;
    }

    /// <summary>«Resumir detalles» / «Detalles originales» (<c>btnLoadDetails_Click</c>): alterna y vuelve a aplicar el 332*.</summary>
    public void AlternarResumen(CatalogoCompras catalogo)
    {
        if (DetallesOriginales is null) return;
        var nuevos = Resumido
            ? DetallesOriginales.Select(d => d.Copiar()).ToList()
            : PreparacionCompra.Resumir(DetallesOriginales.Select(d => d.Copiar()).ToList(), Proveedor?.CuentaGasto);
        Detalles.Clear();
        Detalles.AddRange(nuevos);
        Resumido = !Resumido;
        if (FormaPagoId is { } id) PreparacionCompra.AplicarNoRetencion(Detalles, id, catalogo);
    }

    /// <summary>La cuenta de gasto elegida para un proveedor nuevo se propaga a las líneas, impuestos no IVA y propina sin cuenta.</summary>
    public void PropagarCuentaGasto()
    {
        var cuenta = Proveedor?.CuentaGasto;
        if (string.IsNullOrEmpty(cuenta)) return;
        foreach (var d in Detalles.Where(x => string.IsNullOrEmpty(x.CuentaId))) d.CuentaId = cuenta;
        foreach (var d in DetallesOriginales ?? []) if (string.IsNullOrEmpty(d.CuentaId)) d.CuentaId = cuenta;
        foreach (var t in ImpuestosOriginales ?? []) if (t.Codigo != "2" && string.IsNullOrEmpty(t.CuentaId)) t.CuentaId = cuenta;
    }

    public static FormularioCompraEstado Nueva(TipoDocumentoCompra tipo) => new()
    {
        Modo = ModoFormulario.Nueva,
        Tipo = tipo,
        // cmbCodDoc_SelectedIndexChanged: nota de venta → costo; resto → crédito.
        Sustento = tipo == TipoDocumentoCompra.NotaDeVenta ? SustentoCompra.Costo : SustentoCompra.Credito,
        Origen = OrigenCompra.Manual,
        FormaPagoId = FormaPagoRetencion.Otros,
    };

    /// <summary>Desde una OC guardada (<see cref="RecargaOc"/>). En modo copia: fecha hoy, sin nº de factura ni OC (<c>LoadCopyBill</c>).</summary>
    public static FormularioCompraEstado DesdeOc(ResultadoRecarga recarga, OcGuardadaCabecera cabecera, bool copia)
    {
        var e = recarga.Entrada;
        var f = new FormularioCompraEstado
        {
            Modo = copia ? ModoFormulario.Copia : ModoFormulario.Existente,
            Tipo = e.TipoDocumento,
            Sustento = e.Sustento,
            Origen = copia ? OrigenCompra.Manual : e.Origen,
            NumeroFactura = copia ? string.Empty : e.NumeroFactura,
            Autorizacion = copia ? string.Empty : e.Autorizacion,
            FechaEmision = copia ? DateTime.Today : e.FechaEmision,
            FechaRegistro = copia ? DateTime.Today : e.FechaRegistro,
            Proveedor = ProveedorEdicion.Desde(e.Proveedor),
            Propina = e.Propina,
            FormaPagoId = e.FormaPagoId,
            NumeroOcExistente = copia ? null : cabecera.Referencia,
            NumeroRetencionExistente = copia ? null : e.NumeroRetencion,
            ImpuestosOriginales = copia ? null : e.Impuestos,
        };
        f.Detalles.AddRange(e.Detalles.Select(d => d.Copiar()));
        return f;
    }

    public void AgregarLinea(string? cuentaGasto) => Detalles.Add(new LineaDetalle
    {
        Cantidad = 1,
        CodigoIva = "2",
        CodigoPorcentajeIva = "4",
        TarifaIva = 0.15m,
        CuentaId = string.IsNullOrEmpty(cuentaGasto) ? null : cuentaGasto,
    });

    /// <summary>Cambió cantidad o precio: recalcula el subtotal de la línea (setters de <c>PoDetailToLoad</c>).</summary>
    public void Recalcular(LineaDetalle linea)
    {
        linea.MontoSinImpuestos = Math.Round(linea.Cantidad * linea.PrecioUnitario, 2);
        LineasModificadas = true;
    }

    /// <summary>Código de porcentaje de IVA de la línea; la tasa sale de <c>dicTaxRate</c> (<c>PoDetailToLoad.PercentIVACode</c>).</summary>
    public void CambiarIva(LineaDetalle linea, string codigo, CatalogoCompras catalogo)
    {
        linea.CodigoIva = "2";
        linea.CodigoPorcentajeIva = codigo;
        linea.TarifaIva = catalogo.TarifasIva.FirstOrDefault(x => x.CodigoPorcentaje == codigo)?.Tasa ?? 0;
        LineasModificadas = true;
    }

    /// <summary>Forma de pago sin retención → todas las líneas con el R-IRF 332 que corresponda (<c>Twh332CodeUpdate</c>).</summary>
    public void CambiarFormaPago(int id, CatalogoCompras catalogo)
    {
        var anterior = FormaPagoId is { } a ? catalogo.FormaPago(a) : null;
        FormaPagoId = id;
        var nueva = catalogo.FormaPago(id);
        if (nueva is { TieneRetencion: false })
        {
            PreparacionCompra.AplicarNoRetencion(Detalles, id, catalogo);
            foreach (var d in Detalles) d.RetencionIvaId = null;
        }
        else if (anterior is { TieneRetencion: false })
        {
            foreach (var d in Detalles) d.RetencionFuenteId = null;
        }
    }

    public IReadOnlyList<LineaImpuesto> Impuestos(CatalogoCompras catalogo, out IReadOnlyList<string> errores)
    {
        if ((ImpuestosFijos || !LineasModificadas) && ImpuestosOriginales is not null)
        {
            errores = [];
            return ImpuestosOriginales;
        }
        var (lista, e) = PreparacionCompra.ImpuestosDigitados(Detalles, catalogo);
        errores = e;
        return lista;
    }

    public IReadOnlyList<LineaRetencion>? Retenciones(CatalogoCompras catalogo) =>
        FormaPagoId is { } id ? CalculadorRetenciones.Calcular(Detalles, id, Proveedor?.CuentaGasto, catalogo) : null;

    public bool NumeroOcAutomatico => Modo != ModoFormulario.Existente && string.IsNullOrWhiteSpace(NumeroOcManual);
    public bool NumeroRetencionAutomatico => string.IsNullOrWhiteSpace(NumeroRetencionManual);

    /// <summary>El formulario como entrada del armador. <c>null</c> si falta el proveedor.</summary>
    public EntradaCompra? Entrada(string ruc, CatalogoCompras catalogo)
    {
        if (Proveedor is null) return null;
        var numeroOc = !string.IsNullOrWhiteSpace(NumeroOcManual) ? NumeroOcManual.Trim()
            : NumeroOcExistente ?? NumeroOcPrevisto;
        var numeroRet = !string.IsNullOrWhiteSpace(NumeroRetencionManual) ? NumeroRetencionManual.Trim()
            : NumeroRetencionExistente ?? NumeroRetencionPrevisto;
        return new EntradaCompra
        {
            RucEmpresa = ruc,
            TipoDocumento = Tipo,
            Sustento = Sustento,
            Origen = Origen,
            Factura = Factura,
            NumeroFactura = NumeroFactura.Trim(),
            Autorizacion = Autorizacion.Trim(),
            FechaEmision = FechaEmision.Date,
            FechaRegistro = FechaRegistro.Date,
            Proveedor = Proveedor.AProveedorSage(),
            Detalles = Detalles,
            Impuestos = Impuestos(catalogo, out _),
            Propina = Propina,
            FormaPagoId = FormaPagoId,
            Retenciones = Retenciones(catalogo),
            NumeroRetencion = string.IsNullOrEmpty(numeroRet) ? null : numeroRet,
            NumeroOc = numeroOc,
        };
    }
}
