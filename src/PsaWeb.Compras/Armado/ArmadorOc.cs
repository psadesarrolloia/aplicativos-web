using System.Text.Json;
using System.Text.RegularExpressions;
using PsaWeb.Compras.Catalogo;
using PsaWeb.Compras.Sri;

namespace PsaWeb.Compras.Armado;

/// <summary>Todo lo que el digitador dejó en el formulario al pulsar «Guardar».</summary>
public sealed class EntradaCompra
{
    public required string RucEmpresa { get; init; }
    public required TipoDocumentoCompra TipoDocumento { get; init; }
    public required SustentoCompra Sustento { get; init; }
    public required OrigenCompra Origen { get; init; }

    /// <summary>Factura del SRI; <c>null</c> en una compra digitada.</summary>
    public FacturaRecibida? Factura { get; init; }

    /// <summary><c>001-001-000000123</c>. Desde XML se toma de la factura.</summary>
    public required string NumeroFactura { get; init; }

    /// <summary>Clave de acceso (49 dígitos) o autorización de preimpresa (10 dígitos).</summary>
    public required string Autorizacion { get; init; }

    public required DateTime FechaEmision { get; init; }

    /// <summary>Fecha de registro: va a <c>GoodThruDate</c>.</summary>
    public required DateTime FechaRegistro { get; init; }

    public required ProveedorSage Proveedor { get; init; }

    /// <summary>Detalles tal como quedaron en la grilla (originales o resumidos).</summary>
    public required IReadOnlyList<LineaDetalle> Detalles { get; init; }

    public required IReadOnlyList<LineaImpuesto> Impuestos { get; init; }

    public decimal Propina { get; init; }

    /// <summary>Opción de <c>AboutApplyTwh</c>; <c>null</c> si el digitador no eligió ninguna.</summary>
    public int? FormaPagoId { get; init; }

    /// <summary>Grilla de retenciones (<see cref="CalculadorRetenciones.Calcular"/>, editable); <c>null</c> = sin retención.</summary>
    public IReadOnlyList<LineaRetencion>? Retenciones { get; init; }

    public string? NumeroRetencion { get; init; }

    /// <summary><c>OC-8757</c>.</summary>
    public required string NumeroOc { get; init; }
}

public sealed record ResultadoArmado(
    OcArmada? Oc,
    IReadOnlyList<string> Errores,
    IReadOnlyList<string> Avisos,
    IReadOnlyList<string> Confirmaciones)
{
    public bool Correcto => Oc is not null && Errores.Count == 0;
}

/// <summary>
/// Valida y arma la Purchase Order. Port de <c>Bill.CorrectToLoad</c> (ítem C y validaciones), <c>Bill.LoadPurchaseOC</c>
/// (reparto 10/90 de seguros 322) y <c>Sage50us.LoadPurchaseOrder</c> (orden y contenido de las líneas).
/// </summary>
public static class ArmadorOc
{
    public const string CuentaPorPagar = "20000";
    private const int LargoDescripcion = 159;

    public static ResultadoArmado Armar(EntradaCompra e, CatalogoCompras catalogo)
    {
        var errores = new List<string>();
        var avisos = new List<string>();
        var confirmaciones = new List<string>();
        var desdeXml = e.Factura is not null;
        var proveedor = e.Proveedor;

        // Se trabaja sobre copias: el armado asigna ítems C y reparte seguros sin tocar lo que ve el digitador.
        var detalles = e.Detalles.Select(x => x.Copiar()).ToList();
        var impuestos = e.Impuestos.Select(x => new LineaImpuesto
        {
            Codigo = x.Codigo, ItemId = x.ItemId, CuentaId = x.CuentaId, BaseImponible = x.BaseImponible, Valor = x.Valor,
        }).ToList();
        var itemC = new Dictionary<LineaDetalle, string>(ReferenceEqualityComparer.Instance);

        // ---- btnLoad_Click ----
        // Corrección C4: el `.exe` exigía 7 caracteres (xx-xxxx); la numeración nueva pasa de OC-9999 a OC-10000.
        if (!Regex.IsMatch(e.NumeroOc, @"^[A-Za-z0-9]{2}-\d{4,}$"))
        {
            errores.Add("Ingrese un número de Orden de Compra correcto xx-xxxx");
        }

        // ---- CorrectToLoad ----
        if (string.IsNullOrWhiteSpace(proveedor.Id))
        {
            errores.Add("Revise la información del proveedor");
        }
        var forma = e.FormaPagoId is { } idForma ? catalogo.FormaPago(idForma) : null;
        if (forma is null)
        {
            errores.Add("Seleccione: Aplica Retención /o/ No Aplica retención");
            return new(null, errores, avisos, confirmaciones);
        }

        var codDoc = TiposDocumentoCompra.Codigo(e.TipoDocumento);
        var sinItem = detalles.Where(x => string.IsNullOrEmpty(x.ItemId)).ToList();
        var haySeguros = sinItem.Any(x => catalogo.RetencionFuente(x.RetencionFuenteId)?.CustomField1 == CalculadorRetenciones.CodigoSeguros);
        var avisoNotaVenta = false;

        foreach (var linea in sinItem)
        {
            var (conIva, tipoSinIva) = linea.CodigoPorcentajeIva switch
            {
                "0" => (false, "Imponible 0%"),
                "6" => (false, "NoGraIVA"),
                "7" => (false, "ImpExe"),
                _ => (true, string.Empty), // 2, 3, 4 (15 %), 5 (5 %), futuros y sin IVA en el XML
            };
            IEnumerable<ItemSage> candidatos = FiltrarPorIva(catalogo.ItemsC, conIva, tipoSinIva);

            if (forma.TieneRetencion)
            {
                candidatos = candidatos.Where(x => x.CustomField2.Contains("RF") && x.CustomField2.Contains("SI"));
                if (!haySeguros && linea.RetencionFuenteId is null && linea.RetencionIvaId is null)
                {
                    errores.Add($"Ver detalle {linea.Descripcion}, según la forma de pago seleccionada debería aplicar retenciones");
                }
                if (codDoc == "02" && !avisoNotaVenta)
                {
                    errores.Add("El tipo de comprobante \"NOTA DE VENTA\" NO APLICA RETENCIONES, seleccione la opción correcta.");
                    avisoNotaVenta = true;
                }
                if (!string.IsNullOrEmpty(linea.RetencionFuenteId)
                    && catalogo.ItemsRetencionFuente.Where(x => x.Id == linea.RetencionFuenteId).ToList() is [var ret]
                    && ret.CustomField1.Contains("332"))
                {
                    candidatos = FiltrarPorIva(catalogo.ItemsC, conIva, tipoSinIva)
                        .Where(x => x.CustomField2.Contains("RF") && x.CustomField2.Contains("NO") && x.CustomField5 == ret.CustomField1);
                }
            }
            else
            {
                var codigo332 = Codigo332(forma.Id);
                candidatos = candidatos.Where(x => x.CustomField2.Contains("RF") && x.CustomField2.Contains("NO") && x.CustomField5 == codigo332);
                if (linea.RetencionFuenteId is not null
                    && catalogo.RetencionFuente(linea.RetencionFuenteId)?.CustomField1.Contains("332") != true)
                {
                    errores.Add($"Ver detalle {linea.Descripcion}, según la forma de pago seleccionada no debería aplicar retenciones");
                }
            }

            if (candidatos.FirstOrDefault() is { } elegido)
            {
                itemC[linea] = elegido.Id;
                avisos.Add($"Se asigna un Item(Cs) {{{elegido.Id}}} para el detalle {linea.Descripcion}");
            }
            else
            {
                avisos.Add($"No se pudo encontrar un Item(Cs) para el detalle {linea.Descripcion}");
            }
        }

        if (!(e.Autorizacion.All(char.IsDigit) && e.Autorizacion.Length is 10 or 49))
        {
            errores.Add("NUMERO de AUTORIZACION de factura incorrecto");
        }

        if (!desdeXml)
        {
            if (!EsNumeroFacturaValido(e.NumeroFactura)) errores.Add("NUMERO de factura incorrecto");
            if (string.IsNullOrEmpty(proveedor.Identificacion)) errores.Add("Revise la información del PROVEEDOR: identificación del proveedor es requerido");
            if (string.IsNullOrEmpty(proveedor.TipoIdentificacionSri)) errores.Add("Revise la información del PROVEEDOR: No se pudo determinar correctamente el Tipo de identifcación");
        }
        if (string.IsNullOrEmpty(proveedor.Email) && forma.TieneRetencion) errores.Add("Revise la información del PROVEEDOR: email del proveedor es requerido");
        if (!desdeXml && string.IsNullOrEmpty(proveedor.NombreCompleto)) errores.Add("Revise la información del PROVEEDOR: Ingrese Nombre");
        if (string.IsNullOrEmpty(proveedor.CuentaGasto)) errores.Add("Revise la información del PROVEEDOR: cuenta de gasto es requerida");
        if (!desdeXml && detalles.Count == 0) errores.Add("Ingrese DETALLES de factura");

        if (ErrorProveedorExterior(proveedor) is { } errorExterior)
        {
            errores.Add("PROVEEDOR del exterior:" + errorExterior);
        }
        if (codDoc == "03")
        {
            if (proveedor.TipoIdentificacionSri == "04") errores.Add("PROVEEDOR con RUC: No puede ingresar una liquidación en compra.");
        }
        else if (proveedor.EsDelExterior)
        {
            errores.Add("PROVEEDOR del exterior: Debe ingresar liquidación en compra.");
        }
        else if (proveedor.TipoIdentificacionSri == "05")
        {
            errores.Add("El proveedor con cédula se usa para Liquidación en compra; solo en reposición de caja chica.");
        }

        // Ítem C de los impuestos que no son IVA (ICE…) y de la propina.
        var codigo332Impuestos = Codigo332(forma.Id);
        var otrosImpuestos = impuestos.Where(x => x.ItemId is null && x.Codigo != "2").ToList();
        if (otrosImpuestos.Count > 0)
        {
            var itemImpuesto = catalogo.ItemsC.FirstOrDefault(x => x.CustomField2.Contains("RF") && x.CustomField2.Contains("NO")
                && x.CustomField3.Contains("IVA") && x.CustomField3.Contains("NO") && x.CustomField4 == "NoGraIVA" && x.CustomField5 == codigo332Impuestos);
            var baseIva = impuestos.Where(x => x.Codigo == "2").Sum(x => x.BaseImponible);
            // Bug B1 (fiel): compara el código de impuesto (2 o vacío) en vez del código de porcentaje, así que suma todas las líneas.
            var baseDetalles = sinItem.Where(x => x.CodigoIva is not "0" and not "6" and not "7").Sum(x => x.MontoSinImpuestos);
            if (baseDetalles < baseIva)
            {
                itemImpuesto = catalogo.ItemsC.FirstOrDefault(x => x.CustomField2.Contains("RF") && x.CustomField2.Contains("NO")
                    && x.CustomField3.Contains("IVA") && x.CustomField3.Contains("SI") && x.CustomField5 == codigo332Impuestos);
            }
            foreach (var imp in otrosImpuestos) imp.ItemId = itemImpuesto?.Id;
        }

        string? itemPropina = null;
        string? cuentaPropina = null;
        if (e.Propina > 0)
        {
            itemPropina = catalogo.ItemsC.FirstOrDefault(x => x.CustomField2.Contains("RF") && x.CustomField2.Contains("NO")
                && x.CustomField3.Contains("IVA") && x.CustomField3.Contains("NO") && x.CustomField4 == "NoGraIVA")?.Id;
            cuentaPropina = string.IsNullOrEmpty(proveedor.CuentaGasto) ? null : proveedor.CuentaGasto;
        }

        string? ItemDe(LineaDetalle x) => string.IsNullOrEmpty(x.ItemId) ? itemC.GetValueOrDefault(x) : x.ItemId;
        if (detalles.Any(x => ItemDe(x) is null)) errores.Add("No se pudo Identificar correctamente el Item(Cs)");
        if (detalles.Any(x => string.IsNullOrEmpty(x.CuentaId))) errores.Add("Revise los detalles de la compra, falta asignar Cuenta");
        if (impuestos.Any(x => x.ItemId is null || string.IsNullOrEmpty(x.CuentaId))) errores.Add("Seleccione Cuenta válida en todos los detalles para contabilizar la compra: revise los impuestos de la compra");
        if (e.Propina > 0 && (cuentaPropina is null || itemPropina is null)) errores.Add("Revise la cuenta correspondiente a la propina de la compra");

        var retenciones = e.Retenciones?.ToList();
        if (forma.TieneRetencion)
        {
            if (retenciones is null) errores.Add("Debe especificar los detalles de retención");
            else if (forma.Id == FormaPagoRetencion.RetencionAsumida && retenciones.Any(x => x.CuentaAsumida.Length == 0))
            {
                errores.Add("Ha ingresado detalles de retención asumida, entonces debe indicar la cuenta de gasto para el efecto");
            }
        }

        // DontFoundAnyItem: los ítems elegidos a mano deben existir en Sage.
        foreach (var x in detalles.Where(x => !string.IsNullOrEmpty(x.ItemId)))
        {
            if (!catalogo.ItemsDetalle.Any(i => i.Id == x.ItemId)) errores.Add($"No existe registrado en sage un Item con ID: {x.ItemId}");
        }
        if (catalogo.ItemAutorizacion is null) errores.Add("No existe el ítem AUT-SRI activo en Sage.");

        if (desdeXml && e.Factura!.Comprador.Identificacion != e.RucEmpresa)
        {
            confirmaciones.Add("La presente factura es para a una empresa diferente, Confirma que desea cargar la compra?");
        }
        if (desdeXml && e.Factura!.EsAmbientePruebas) avisos.Add("La factura es del ambiente de pruebas del SRI.");
        if (desdeXml)
        {
            // §8: el `.exe` no lo controlaba (en CPTDC hay OC con el monto de un detalle cambiado a mano que no cuadran).
            var suma = detalles.Sum(x => x.MontoSinImpuestos) + impuestos.Sum(x => x.Valor) + e.Propina;
            if (suma != e.Factura!.Totales.ImporteTotal)
            {
                avisos.Add($"La suma de la compra ({suma:N2}) no coincide con el total de la factura ({e.Factura.Totales.ImporteTotal:N2}).");
            }
        }

        if (errores.Count > 0) return new(null, errores, avisos, confirmaciones);

        // ---- LoadPurchaseOC: seguros 322 = 10 % con retención + 90 % a un ítem C sin retención ----
        var itemSeguros = detalles.Where(x => x.RetencionFuenteId is not null)
            .LastOrDefault(x => catalogo.RetencionFuente(x.RetencionFuenteId)?.CustomField1 == CalculadorRetenciones.CodigoSeguros)?.RetencionFuenteId;
        if (itemSeguros is not null)
        {
            var nuevas = new List<LineaDetalle>();
            foreach (var linea in detalles)
            {
                var actual = catalogo.ItemsC.FirstOrDefault(x => x.Id == ItemDe(linea));
                if (actual is null)
                {
                    errores.Add($"En el Item ({linea.Descripcion}), no se pudo determinar un codigo Item de Gasto sobre el 90% que no aplica retención con código {CalculadorRetenciones.CodigoSeguros}");
                    return new(null, errores, avisos, confirmaciones);
                }
                var sinRetencion = catalogo.ItemsC.FirstOrDefault(x => x.CustomField3 == actual.CustomField3
                    && x.CustomField2.Contains("RF") && x.CustomField2.Contains("NO") && x.CustomField4 == actual.CustomField4
                    && !x.CustomField5.Contains('G') && !x.CustomField5.Contains('I'));
                if (sinRetencion is null)
                {
                    errores.Add($"En el Item ({linea.Descripcion}), no se pudo determinar un codigo Item de Gasto  sobre el 90% que no aplica retención con código {CalculadorRetenciones.CodigoSeguros}");
                    return new(null, errores, avisos, confirmaciones);
                }
                if (linea.RetencionFuenteId == itemSeguros)
                {
                    // Bug B2 (fiel): los setters de PoDetailToLoad dejan el 10 % multiplicado por la cantidad original;
                    // con cantidad 1 (lo normal en seguros) es exacto, y el total de la OC siempre cuadra.
                    var total = linea.MontoSinImpuestos;
                    var diezPorCiento = Math.Round(total / 10, 2) * linea.Cantidad;
                    linea.Cantidad = 1;
                    linea.MontoSinImpuestos = diezPorCiento;
                    linea.PrecioUnitario = diezPorCiento;
                    var noventa = new LineaDetalle
                    {
                        Descripcion = linea.Descripcion, Cantidad = 1, PrecioUnitario = total - diezPorCiento,
                        MontoSinImpuestos = total - diezPorCiento, CuentaId = linea.CuentaId,
                    };
                    itemC[noventa] = sinRetencion.Id;
                    nuevas.Add(noventa);
                }
                else if (string.IsNullOrEmpty(linea.RetencionFuenteId))
                {
                    itemC[linea] = sinRetencion.Id;
                    linea.ItemId = null;
                }
            }
            detalles.AddRange(nuevas);
            if (retenciones is not null && retenciones.Where(x => x.Valor == 0).ToList() is [var cero])
            {
                retenciones[retenciones.IndexOf(cero)] = cero with { BaseImponible = cero.BaseImponible + nuevas.Sum(x => x.MontoSinImpuestos) };
            }
        }

        // ---- LoadPurchaseOrder: líneas en el orden del `.exe` ----
        var lineas = new List<LineaOc>();
        foreach (var d in detalles)
        {
            var descripcion = d.Descripcion.Length > LargoDescripcion ? d.Descripcion[..LargoDescripcion] : d.Descripcion;
            lineas.Add(new LineaOc(TipoLineaOc.Detalle, ItemDe(d), descripcion, d.Cantidad,
                d.Cantidad == 0 ? 0 : d.MontoSinImpuestos / d.Cantidad, d.MontoSinImpuestos, d.CuentaId, d.JobId));
        }
        foreach (var t in impuestos)
        {
            var descripcion = catalogo.TiposImpuesto.GetValueOrDefault(t.Codigo) ?? string.Empty;
            lineas.Add(t.Codigo == "2"
                ? new LineaOc(TipoLineaOc.Impuesto, t.ItemId, descripcion, t.BaseImponible,
                    t.BaseImponible == 0 ? 0 : t.Valor / t.BaseImponible, t.Valor, t.CuentaId, null)
                : new LineaOc(TipoLineaOc.Impuesto, t.ItemId, descripcion, 1, t.Valor, t.Valor, t.CuentaId, null));
        }
        if (e.Propina > 0)
        {
            lineas.Add(new LineaOc(TipoLineaOc.Propina, itemPropina, "Propina / Otros", 1, e.Propina, e.Propina, cuentaPropina, null));
        }
        lineas.Add(new LineaOc(TipoLineaOc.Autorizacion, catalogo.ItemAutorizacion!.Id, e.Autorizacion, 0, 0, 0, catalogo.PrimeraCuenta, null));
        if (retenciones is not null)
        {
            foreach (var r in retenciones)
            {
                var item = catalogo.Retencion(r.ItemId);
                var porcentaje = item?.PorcentajeRetencion ?? 0m;
                lineas.Add(new LineaOc(TipoLineaOc.Retencion, r.ItemId, item?.Descripcion ?? string.Empty, r.BaseImponible,
                    PrecioRetencion(r.BaseImponible, porcentaje), Math.Round(r.BaseImponible * porcentaje, 2), r.CuentaId, null));
            }
            foreach (var r in retenciones.Where(x => x.CuentaAsumida.Length > 0))
            {
                var item = catalogo.Retencion(r.ItemId);
                var porcentaje = -(item?.PorcentajeRetencion ?? 0m);
                lineas.Add(new LineaOc(TipoLineaOc.RetencionAsumida, null, item?.Descripcion ?? string.Empty, r.BaseImponible,
                    PrecioRetencion(r.BaseImponible, porcentaje), Math.Round(r.BaseImponible * porcentaje, 2), r.CuentaAsumida, null));
            }
        }
        lineas.Add(new LineaOc(TipoLineaOc.Relleno, null, ".", 0, 0, 0, null, null));
        lineas.Add(new LineaOc(TipoLineaOc.Relleno, null, ".", 0, 0, 0, null, null));

        var oc = new OcArmada(
            Referencia: e.NumeroOc,
            ProveedorId: proveedor.Id,
            NombreProveedor: proveedor.Nombre,
            DireccionProveedor1: proveedor.Direccion1,
            DireccionProveedor2: proveedor.Direccion2,
            IdentificacionProveedor: proveedor.Pais,
            Fecha: e.FechaEmision,
            FechaRegistro: e.FechaRegistro,
            ShipVia: TiposDocumentoCompra.Descripcion(e.TipoDocumento),
            NumeroFactura: e.NumeroFactura,
            NumeroRetencion: retenciones is null ? null : e.NumeroRetencion,
            LlevaRetencion: retenciones is not null,
            EstadoSustento: e.Sustento == SustentoCompra.Credito ? "01" : "02",
            Zip: e.Origen switch { OrigenCompra.Manual => "Manual", OrigenCompra.Externa => "Externo", _ => null },
            CuentaPorPagar: CuentaPorPagar,
            Lineas: lineas);
        return new(oc, errores, avisos, confirmaciones);
    }

    /// <summary>Precio de la línea de retención: el % del ítem, o monto/base si el redondeo del monto lo cambió (PoTwhItems).</summary>
    private static decimal PrecioRetencion(decimal cantidad, decimal porcentaje)
    {
        var exacto = cantidad * porcentaje;
        var monto = Math.Round(exacto, 2);
        return monto != exacto ? monto / cantidad : porcentaje;
    }

    private static IEnumerable<ItemSage> FiltrarPorIva(IEnumerable<ItemSage> items, bool conIva, string tipoSinIva) => conIva
        ? items.Where(x => x.CustomField3.Contains("IVA") && x.CustomField3.Contains("SI"))
        : items.Where(x => x.CustomField3.Contains("IVA") && x.CustomField3.Contains("NO") && x.CustomField4.Contains(tipoSinIva));

    /// <summary>Código 332 según la forma de pago: tarjeta → 332G, débito autorizado → 332I, resto → 332.</summary>
    public static string Codigo332(int formaPagoId) => formaPagoId switch
    {
        FormaPagoRetencion.TarjetaCredito => "332G",
        FormaPagoRetencion.DebitoAutorizado => "332I",
        _ => "332",
    };

    /// <summary><c>isValidNumberBill</c>: <c>000-000-</c> + 1 a 9 dígitos, secuencial distinto de cero.</summary>
    public static bool EsNumeroFacturaValido(string numero) =>
        numero.Length is > 8 and < 18
        && numero[..3].All(char.IsDigit) && numero[3] == '-'
        && numero[4..7].All(char.IsDigit) && numero[7] == '-'
        && numero[8..].All(char.IsDigit)
        && long.Parse(numero[8..]) != 0;

    /// <summary>
    /// Datos de pago al exterior (<c>ExternalVendorInfo.IsValid</c> sobre el JSON de 7 valores de <c>Vendors.CustomField1</c>).
    /// Solo aplica a pasaporte (06) e identificación del exterior (08).
    /// </summary>
    public static string? ErrorProveedorExterior(ProveedorSage proveedor)
    {
        if (!proveedor.EsDelExterior) return null;
        string[] v = [];
        try
        {
            if (JsonSerializer.Deserialize<string[]>(proveedor.CustomField1) is { Length: 7 } arr) v = arr;
        }
        catch (JsonException) { }
        var error = string.Empty;
        var tipo = v.Length == 7 ? v[0] : string.Empty;
        var pago = v.Length == 7 ? v[1] : string.Empty;
        if (tipo is not ("01" or "02")) error = "\nTipo de identificación es REQUERIDO.";
        if (pago is not ("01" or "02")) error += "\nMétodo de pago es REQUERIDO.";
        else if (pago == "02")
        {
            if (string.IsNullOrEmpty(v[4])) error += "\nCódigo del país es REQUERIDO.";
            if (v[5] is not ("SI" or "NO")) error += "\nINDIQUE si tiene convenio de doble tributación.";
        }
        return error.Length == 0 ? null : error;
    }
}
