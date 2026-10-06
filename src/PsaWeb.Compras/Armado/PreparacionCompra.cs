using PsaWeb.Compras.Catalogo;
using PsaWeb.Compras.Sri;

namespace PsaWeb.Compras.Armado;

/// <summary>
/// Lo que el `.exe` precarga al abrir una factura del SRI (<c>Bill.LoadInfoBill</c> / <c>LoadDetails</c> /
/// <c>LoadingPaymentsAboutTwh</c> / <c>Twh332CodeUpdate</c> / <c>UpdateTaxLines</c>). El digitador luego edita.
/// </summary>
public static class PreparacionCompra
{
    /// <summary>Origen según el comprador del XML (<c>LoadInfoBill</c>).</summary>
    public static OrigenCompra Origen(FacturaRecibida factura, string rucEmpresa) =>
        factura.Comprador.Identificacion != rucEmpresa ? OrigenCompra.Externa : OrigenCompra.Propia;

    /// <summary>
    /// Detalles desde el XML (<c>LoadDetails</c>): solo los de total &gt; 0, cuenta = cuenta de gasto del proveedor, y el ítem
    /// aprendido en <c>VendorConfiguration</c> si existe en Sage (con su cuenta de inventario).
    /// </summary>
    public static List<LineaDetalle> Detalles(
        FacturaRecibida factura,
        string? cuentaGastoProveedor,
        IReadOnlyList<ConfiguracionItemProveedor> configuraciones,
        CatalogoCompras catalogo)
    {
        var lineas = new List<LineaDetalle>();
        foreach (var d in factura.Detalles.Where(x => x.PrecioTotalSinImpuesto > 0))
        {
            var linea = new LineaDetalle
            {
                Descripcion = d.Descripcion,
                CodigoPrincipal = d.CodigoPrincipal,
                CodigoAuxiliar = d.CodigoAuxiliar,
                Cantidad = d.Cantidad,
                PrecioUnitario = d.PrecioUnitario,
                Descuento = d.Descuento,
                MontoSinImpuestos = d.PrecioTotalSinImpuesto,
                CuentaId = string.IsNullOrEmpty(cuentaGastoProveedor) ? null : cuentaGastoProveedor,
            };
            // PoDetailToLoad(Item): el último impuesto de código 2 define el IVA de la línea.
            foreach (var imp in d.Impuestos.Where(x => x.Codigo == "2"))
            {
                linea.CodigoIva = imp.Codigo;
                linea.CodigoPorcentajeIva = imp.CodigoPorcentaje;
                linea.TarifaIva = imp.Tarifa;
            }
            lineas.Add(linea);
        }

        if (lineas.Any(x => !string.IsNullOrWhiteSpace(x.CodigoPrincipal)))
        {
            foreach (var linea in lineas)
            {
                var config = configuraciones.FirstOrDefault(x => x.CodigoProveedor == linea.CodigoPrincipal);
                var item = config is null ? null : catalogo.ItemsDetalle.FirstOrDefault(x => x.Id == config.ItemSageId);
                if (item is null) continue;
                linea.ItemId = item.Id;
                if (item.CuentaInventario is not null) linea.CuentaId = item.CuentaInventario;
            }
        }
        return lineas;
    }

    /// <summary>
    /// «Resumir detalles» (<c>LoadDetails</c>, lista <c>groupedItems</c>): una línea por (IVA, tarifa, código de porcentaje,
    /// retenciones), cantidad 1, monto = suma redondeada a 2, código <c>Gruoped000…</c>, sin descripción. Con un solo
    /// detalle devuelve el mismo.
    /// </summary>
    public static List<LineaDetalle> Resumir(IReadOnlyList<LineaDetalle> detalles, string? cuentaGastoProveedor)
    {
        if (detalles.Count == 1) return [detalles[0]];
        var resumen = new List<LineaDetalle>();
        var j = 0;
        foreach (var g in detalles.GroupBy(x => (x.CodigoIva, x.TarifaIva, x.CodigoPorcentajeIva, x.RetencionIvaId, x.RetencionFuenteId)))
        {
            var monto = Math.Round(g.Sum(x => x.MontoSinImpuestos), 2);
            var linea = new LineaDetalle
            {
                CodigoPrincipal = "Gruoped" + j.ToString("000"),
                Descripcion = string.Empty,
                Cantidad = 1,
                PrecioUnitario = monto,
                MontoSinImpuestos = monto,
                CuentaId = string.IsNullOrEmpty(cuentaGastoProveedor) ? null : cuentaGastoProveedor,
            };
            if (g.Key.CodigoIva == "2")
            {
                linea.CodigoIva = "2";
                linea.CodigoPorcentajeIva = g.Key.CodigoPorcentajeIva;
                linea.TarifaIva = g.Key.TarifaIva;
            }
            resumen.Add(linea);
            j++;
        }
        return resumen;
    }

    /// <summary>
    /// Forma de pago sugerida (<c>LoadingPaymentsAboutTwh</c>): el primer pago del XML decide; si su código de Datil está en
    /// <c>AboutApplyTwh</c> se usa esa opción, si no «Otros» (5). Sin pagos, «Otros».
    /// </summary>
    public static int FormaPagoSugerida(FacturaRecibida factura, CatalogoCompras catalogo)
    {
        var primero = factura.Pagos?.FirstOrDefault();
        if (primero is null) return FormaPagoRetencion.Otros;
        var datil = catalogo.TiposPagoSri.FirstOrDefault(x => x.CodigoSri == primero.FormaPago)?.CodigoDatil;
        var conRetencion = catalogo.FormasPago.FirstOrDefault(x => x.TieneRetencion && datil is not null && x.CodigoDatil == datil);
        var sinRetencion = catalogo.FormasPago.FirstOrDefault(x => !x.TieneRetencion && datil is not null && x.CodigoDatil == datil);
        return (conRetencion ?? sinRetencion)?.Id ?? FormaPagoRetencion.Otros;
    }

    /// <summary>
    /// «No aplica retención» (<c>Twh332CodeUpdate</c>): con una forma de pago sin retención, todas las líneas llevan el
    /// ítem R-IRF 0 % del código 332 que corresponda (tarjeta → 332G, débito → 332I, resto de ids &lt; 8 → 332); si no hay
    /// exactamente uno, la retención queda vacía. Con forma de pago con retención no toca nada.
    /// </summary>
    public static void AplicarNoRetencion(IEnumerable<LineaDetalle> detalles, int formaPagoId, CatalogoCompras catalogo)
    {
        var forma = catalogo.FormaPago(formaPagoId);
        if (forma is null || forma.TieneRetencion) return;
        var codigo = formaPagoId switch
        {
            FormaPagoRetencion.TarjetaCredito => "332G",
            FormaPagoRetencion.DebitoAutorizado => "332I",
            < 8 => "332",
            _ => null,
        };
        var candidatos = codigo is null ? [] : catalogo.ItemsRetencionFuente.Where(x => x.CustomField1 == codigo).ToList();
        foreach (var linea in detalles)
        {
            linea.RetencionFuenteId = candidatos.Count == 1 ? candidatos[0].Id : null;
        }
    }

    /// <summary>
    /// Líneas de impuesto desde los totales del XML (<c>LoadInfoBill</c>). IVA: ítem IMPUESTO cuyo <c>CustomField3</c>
    /// contiene la tarifa como palabra (15 en «15%»), cuenta = cuenta de inventario del ítem. Otros impuestos (ICE…):
    /// sin ítem (se asigna un ítem C al validar) y cuenta de gasto del proveedor.
    /// </summary>
    public static (List<LineaImpuesto> Impuestos, List<string> Errores) Impuestos(
        FacturaRecibida factura, string? cuentaGastoProveedor, CatalogoCompras catalogo)
    {
        var impuestos = new List<LineaImpuesto>();
        var errores = new List<string>();
        char[] separadores = [' ', '%', ',', '.', ';', ':', '-', '_'];
        foreach (var t in factura.Totales.Impuestos.Where(x => x.Valor > 0))
        {
            if (t.Codigo == "2" && catalogo.ItemsImpuesto.Count > 0)
            {
                // Convert.ToInt32 del `.exe`: redondeo al par.
                var tarifa = t.Tarifa is not { } tf ? 0 : (int)Math.Round(tf < 0 ? tf * 100 : tf, MidpointRounding.ToEven);
                var item = catalogo.ItemsImpuesto.FirstOrDefault(x =>
                    x.CustomField3.Split(separadores, StringSplitOptions.RemoveEmptyEntries).Contains(tarifa.ToString()));
                // Corrección C2: si ningún ítem IMPUESTO calza con la tarifa, el `.exe` usaba el primero (el IVA quedaba en
                // el ítem de otra tarifa). Acá es un error.
                if (item is null)
                {
                    errores.Add($"No hay un ítem de IVA ({tarifa}%) activo en Sage: revisa la categoría IMPUESTO.");
                }
                impuestos.Add(new LineaImpuesto
                {
                    Codigo = t.Codigo,
                    BaseImponible = t.BaseImponible,
                    Valor = t.Valor,
                    ItemId = item?.Id,
                    CuentaId = item?.CuentaInventario,
                });
            }
            else
            {
                impuestos.Add(new LineaImpuesto
                {
                    Codigo = t.Codigo,
                    BaseImponible = t.BaseImponible,
                    Valor = t.Valor,
                    CuentaId = string.IsNullOrEmpty(cuentaGastoProveedor) ? null : cuentaGastoProveedor,
                });
            }
        }
        return (impuestos, errores);
    }

    /// <summary>
    /// IVA de una compra digitada (<c>UpdateTaxLines</c>): una línea por código de porcentaje con IVA, base = suma de las líneas,
    /// valor = base × tasa redondeado (lejos de cero), ítem IMPUESTO cuyo <c>CustomField2</c>/<c>CustomField3</c> contiene el
    /// nombre de la tarifa («15%»).
    /// </summary>
    public static (List<LineaImpuesto> Impuestos, List<string> Errores) ImpuestosDigitados(
        IReadOnlyList<LineaDetalle> detalles, CatalogoCompras catalogo)
    {
        var impuestos = new List<LineaImpuesto>();
        var errores = new List<string>();
        foreach (var g in detalles
                     .Where(x => x.CodigoPorcentajeIva is not null and not "0" and not "6" and not "7")
                     .GroupBy(x => x.CodigoPorcentajeIva!))
        {
            var tarifa = catalogo.TarifasIva.FirstOrDefault(x => x.CodigoPorcentaje == g.Key);
            if (tarifa is null)
            {
                errores.Add($"Código de IVA {g.Key} desconocido.");
                continue;
            }
            var item = catalogo.ItemsImpuesto.FirstOrDefault(x => x.CustomField2.Contains(tarifa.Nombre) || x.CustomField3.Contains(tarifa.Nombre));
            if (item is null)
            {
                errores.Add($"Item de IVA correspondiente a {tarifa.Nombre} no registrado en SAGE");
                continue;
            }
            var baseImponible = g.Sum(x => x.MontoSinImpuestos);
            impuestos.Add(new LineaImpuesto
            {
                Codigo = "2",
                BaseImponible = baseImponible,
                // Corrección C3: el `.exe` multiplicaba por el float de dicTaxRate (0.15000001); acá la tasa exacta.
                Valor = Math.Round(baseImponible * tarifa.Tasa, 2, MidpointRounding.AwayFromZero),
                ItemId = item.Id,
                CuentaId = item.CuentaInventario,
            });
        }
        return (impuestos, errores);
    }
}
