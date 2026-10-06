using PsaWeb.Compras.Armado;
using PsaWeb.Compras.Catalogo;

namespace PsaWeb.Compras.Sage;

/// <summary>Formulario reconstruido desde una OC guardada.</summary>
/// <param name="Avisos">Lo que no se pudo deducir o no cuadra (el digitador debe revisarlo antes de guardar).</param>
public sealed record ResultadoRecarga(EntradaCompra Entrada, IReadOnlyList<string> Avisos);

/// <summary>
/// Vuelve a armar el formulario de compra desde una OC guardada en Sage (para ver, actualizar o copiar).
/// Corrección C9: el `.exe` (<c>LoadingSavedInfoForReuse</c>) recargaba los detalles con IVA 12 % fijo, sin las retenciones
/// de cada línea ni la propina (quedaba como un detalle más) y el digitador tenía que volver a elegirlas. Acá se deducen de
/// las filas de la OC: tarifa desde la línea de IVA, retención de renta por el ítem C y la suma de bases, retención de IVA
/// por su base, forma de pago por las retenciones (332G → tarjeta, 332I → débito, asumida → 7…).
/// </summary>
public static class RecargaOc
{
    public static ResultadoRecarga Reconstruir(OcGuardada oc, ProveedorSage proveedor, CatalogoCompras catalogo, string rucEmpresa)
    {
        var avisos = new List<string>();
        var filas = oc.Filas;
        var itemsC = catalogo.ItemsC.ToDictionary(x => x.Id);
        var iAut = filas.ToList().FindIndex(f => f.ItemId == "AUT-SRI");
        if (iAut < 0)
        {
            avisos.Add("La OC no tiene la línea AUT-SRI.");
            iAut = filas.ToList().FindIndex(f => f.Categoria is "R-IRF" or "R-IVA" || (f.ItemId.Length == 0));
            if (iAut < 0) iAut = filas.Count;
        }

        // ---- Antes de AUT-SRI: detalles, impuestos, propina (en ese orden) ----
        var fin = iAut;
        var propina = 0m;
        if (fin > 0 && filas[fin - 1].Descripcion == "Propina / Otros" && itemsC.ContainsKey(filas[fin - 1].ItemId))
        {
            propina = filas[fin - 1].Monto;
            fin--;
        }
        var nombresImpuesto = catalogo.TiposImpuesto.Where(x => x.Key != "2").ToDictionary(x => x.Value, x => x.Key);
        var inicioImpuestos = fin;
        while (inicioImpuestos > 0)
        {
            var f = filas[inicioImpuestos - 1];
            var esIva = f.Categoria == "IMPUESTO";
            var esOtro = itemsC.ContainsKey(f.ItemId) && nombresImpuesto.ContainsKey(f.Descripcion) && f.Cantidad == 1;
            if (!esIva && !esOtro) break;
            inicioImpuestos--;
        }

        var impuestos = new List<LineaImpuesto>();
        for (var i = inicioImpuestos; i < fin; i++)
        {
            var f = filas[i];
            impuestos.Add(f.Categoria == "IMPUESTO"
                ? new LineaImpuesto { Codigo = "2", ItemId = f.ItemId, CuentaId = f.Cuenta, BaseImponible = f.Cantidad, Valor = f.Monto }
                : new LineaImpuesto { Codigo = nombresImpuesto[f.Descripcion], ItemId = f.ItemId, CuentaId = f.Cuenta, BaseImponible = 0, Valor = f.Monto });
        }

        // Tarifa del IVA de la OC (C9: el `.exe` asumía 12 %).
        var iva = impuestos.FirstOrDefault(x => x.Codigo == "2");
        TarifaIva? tarifa = null;
        if (iva is { BaseImponible: > 0 })
        {
            // Con bases chicas valor/base no da la tarifa (0,24 × 15 % = 0,04 → 16,67 %): se busca la que reproduce el valor.
            tarifa = catalogo.TarifasIva.Where(x => x.Tasa > 0)
                .OrderBy(x => Math.Abs(iva.BaseImponible * x.Tasa - iva.Valor))
                .FirstOrDefault(x => Math.Abs(iva.BaseImponible * x.Tasa - iva.Valor) <= Math.Max(0.02m, iva.Valor * 0.01m));
            if (tarifa is null) avisos.Add($"No se reconoce la tarifa de IVA de la OC ({iva.Valor / iva.BaseImponible:P2}).");
        }

        var detalles = new List<LineaDetalle>();
        var baseIvaRestante = iva?.BaseImponible ?? 0m;
        for (var i = 0; i < inicioImpuestos; i++)
        {
            var f = filas[i];
            var linea = new LineaDetalle
            {
                Descripcion = f.Descripcion,
                Cantidad = f.Cantidad == 0 ? 1 : f.Cantidad,
                MontoSinImpuestos = f.Monto,
                PrecioUnitario = f.Cantidad == 0 ? f.Monto : f.Monto / f.Cantidad,
                CuentaId = f.Cuenta,
                JobId = f.Job.Length == 0 ? null : f.Job,
                ItemId = itemsC.ContainsKey(f.ItemId) ? null : f.ItemId,
            };
            var itemC = itemsC.GetValueOrDefault(f.ItemId);
            var conIva = itemC is not null
                ? !(itemC.CustomField3.Contains("IVA") && itemC.CustomField3.Contains("NO"))
                : tarifa is not null && baseIvaRestante >= f.Monto;
            if (conIva && tarifa is not null)
            {
                linea.CodigoIva = "2";
                linea.CodigoPorcentajeIva = tarifa.CodigoPorcentaje;
                linea.TarifaIva = tarifa.Tasa * 100;
                baseIvaRestante -= f.Monto;
            }
            else if (!conIva)
            {
                linea.CodigoIva = "2";
                linea.CodigoPorcentajeIva = itemC?.CustomField4 switch
                {
                    { } s when s.Contains("ImpExe") => "7",
                    { } s when s.Contains("NoGraIVA") => "6",
                    _ => "0",
                };
            }
            detalles.Add(linea);
        }

        // ---- Después de AUT-SRI: retenciones ----
        var posteriores = filas.Skip(iAut + 1).ToList();
        var filasRf = posteriores.Where(f => f.Categoria == "R-IRF").ToList();
        var filasDetalle = filas.Take(inicioImpuestos).ToList();

        // Seguros 322: el armado agregó al final una línea «90 %» (ítem C sin retención, misma descripción) por cada línea
        // con retención 322. Se vuelven a unir para que guardar de nuevo no reparta dos veces.
        if (filasRf.Any(f => catalogo.RetencionFuente(f.ItemId)?.CustomField1 == CalculadorRetenciones.CodigoSeguros))
        {
            for (var k = detalles.Count - 1; k > 0; k--)
            {
                if (!itemsC.TryGetValue(filasDetalle[k].ItemId, out var ck) || !ck.CustomField2.Contains("NO")) continue;
                var i = Enumerable.Range(0, k).FirstOrDefault(j => detalles[j].Descripcion == detalles[k].Descripcion
                    && itemsC.TryGetValue(filasDetalle[j].ItemId, out var cj) && cj.CustomField2.Contains("SI"), -1);
                if (i < 0) continue;
                var total = detalles[i].MontoSinImpuestos + detalles[k].MontoSinImpuestos;
                detalles[i].Cantidad = 1;
                detalles[i].MontoSinImpuestos = total;
                detalles[i].PrecioUnitario = total;
                detalles.RemoveAt(k);
                filasDetalle.RemoveAt(k);
            }
        }
        var filasRiva = posteriores.Where(f => f.Categoria == "R-IVA").ToList();
        var asumidas = posteriores.Any(f => f.ItemId.Length == 0 && f.Descripcion != ".");
        var formaPago = DeducirFormaPago(filasRf, filasRiva, asumidas, catalogo);
        if (!AsignarRetencionFuente(detalles, filasDetalle, filasRf, itemsC, catalogo))
        {
            avisos.Add("No se pudo deducir la retención de renta de cada línea: revísala.");
        }
        if (!AsignarRetencionIva(detalles, filasRiva)) avisos.Add("No se pudo deducir la retención de IVA de cada línea: revísala.");

        var cab = oc.Cabecera;
        var retenciones = CalculadorRetenciones.Calcular(detalles, formaPago, proveedor.CuentaGasto, catalogo);
        var enSage = filasRf.Concat(filasRiva).Select(f => (f.ItemId, f.Cantidad)).OrderBy(x => x.ItemId).ToList();
        var calculadas = (retenciones ?? []).Select(r => (r.ItemId, r.BaseImponible)).OrderBy(x => x.ItemId).ToList();
        if (!enSage.SequenceEqual(calculadas))
        {
            avisos.Add("Las retenciones recalculadas no coinciden con las de la OC en Sage: revisa la pestaña Retención.");
        }

        var entrada = new EntradaCompra
        {
            RucEmpresa = rucEmpresa,
            TipoDocumento = cab.ShipVia switch
            {
                "NOTA DE VENTA" => TipoDocumentoCompra.NotaDeVenta,
                "LIQUIDACION" => TipoDocumentoCompra.Liquidacion,
                _ => TipoDocumentoCompra.Factura,
            },
            Sustento = cab.Estado == "01" ? SustentoCompra.Credito : SustentoCompra.Costo,
            Origen = cab.Zip switch { "Manual" => OrigenCompra.Manual, "Externo" => OrigenCompra.Externa, _ => OrigenCompra.Propia },
            Factura = null,
            NumeroFactura = cab.Factura,
            Autorizacion = oc.Autorizacion,
            FechaEmision = cab.Fecha,
            FechaRegistro = cab.FechaRegistro ?? cab.Fecha,
            Proveedor = proveedor,
            Detalles = detalles,
            Impuestos = impuestos,
            Propina = propina,
            FormaPagoId = formaPago,
            Retenciones = retenciones,
            NumeroRetencion = cab.Retencion.Length == 0 ? null : cab.Retencion,
            NumeroOc = cab.Referencia,
        };
        return new ResultadoRecarga(entrada, avisos);
    }

    /// <summary>Forma de pago por las retenciones: asumida → 7; solo 332G/332I/332 → tarjeta/débito/no sujeto; sin retención → «Resolución 8»; resto → Otros.</summary>
    public static int DeducirFormaPago(IReadOnlyList<OcGuardadaFila> rf, IReadOnlyList<OcGuardadaFila> riva, bool asumidas, CatalogoCompras catalogo)
    {
        if (asumidas) return FormaPagoRetencion.RetencionAsumida;
        if (rf.Count == 0 && riva.Count == 0)
        {
            // Sin filas de retención: forma de pago sin retención y sin 332 (ids ≥ 8).
            return catalogo.FormasPago.FirstOrDefault(x => !x.TieneRetencion && x.Id >= 8)?.Id ?? FormaPagoRetencion.Otros;
        }
        var codigos = rf.Select(f => catalogo.RetencionFuente(f.ItemId)?.CustomField1 ?? string.Empty).ToList();
        if (riva.Count == 0 && codigos.Count > 0 && codigos.All(c => c.StartsWith("332")))
        {
            return codigos[0] switch
            {
                "332G" => FormaPagoRetencion.TarjetaCredito,
                "332I" => FormaPagoRetencion.DebitoAutorizado,
                _ => catalogo.FormasPago.FirstOrDefault(x => !x.TieneRetencion && x.Id is > 2 and < 8 && x.Id != 3)?.Id ?? 6,
            };
        }
        return FormaPagoRetencion.Otros;
    }

    /// <summary>Línea con ítem C «RF: NO» → el R-IRF 332* de su <c>CustomField5</c>; el resto se reparte por suma de bases.</summary>
    public static bool AsignarRetencionFuente(List<LineaDetalle> detalles, IReadOnlyList<OcGuardadaFila> filasDetalle,
        IReadOnlyList<OcGuardadaFila> filasRf, IReadOnlyDictionary<string, ItemSage> itemsC, CatalogoCompras catalogo)
    {
        var libres = new List<int>();
        for (var i = 0; i < detalles.Count; i++)
        {
            if (itemsC.TryGetValue(filasDetalle[i].ItemId, out var c) && c.CustomField2.Contains("NO"))
            {
                detalles[i].RetencionFuenteId = filasRf.FirstOrDefault(f => catalogo.RetencionFuente(f.ItemId)?.CustomField1 == c.CustomField5)?.ItemId;
                if (detalles[i].RetencionFuenteId is null && filasRf.Count > 0) return false;
            }
            else libres.Add(i);
        }
        var pendientes = filasRf.Select(f => (f.ItemId, Base: f.Cantidad - detalles.Where(d => d.RetencionFuenteId == f.ItemId).Sum(d => d.MontoSinImpuestos)))
            .Where(x => x.Base != 0).ToList();
        foreach (var (item, baseImponible) in pendientes)
        {
            var esSeguros = catalogo.RetencionFuente(item)?.CustomField1 == CalculadorRetenciones.CodigoSeguros;
            var subconjunto = SubconjuntoConSuma(libres.Select(i => detalles[i].MontoSinImpuestos).ToList(),
                s => esSeguros ? s * 0.1m == baseImponible : s == baseImponible);
            if (subconjunto is null) return false;
            foreach (var k in subconjunto.OrderByDescending(k => k)) { detalles[libres[k]].RetencionFuenteId = item; libres.RemoveAt(k); }
        }
        return true;
    }

    /// <summary>Cada R-IVA toma las líneas con IVA cuya suma × tarifa (redondeada) da su base.</summary>
    public static bool AsignarRetencionIva(List<LineaDetalle> detalles, IReadOnlyList<OcGuardadaFila> filasRiva)
    {
        var libres = Enumerable.Range(0, detalles.Count).Where(i => detalles[i].TarifaIva > 0).ToList();
        foreach (var f in filasRiva)
        {
            var hallado = false;
            foreach (var tarifa in libres.Select(i => detalles[i].TarifaIva).Distinct().ToList())
            {
                var tasa = tarifa >= 1 ? tarifa / 100m : tarifa;
                var candidatos = libres.Where(i => detalles[i].TarifaIva == tarifa).ToList();
                var subconjunto = SubconjuntoConSuma(candidatos.Select(i => detalles[i].MontoSinImpuestos).ToList(),
                    s => Math.Round(s * tasa, 2, MidpointRounding.AwayFromZero) == f.Cantidad);
                if (subconjunto is null) continue;
                foreach (var k in subconjunto) detalles[candidatos[k]].RetencionIvaId = f.ItemId;
                libres.RemoveAll(i => detalles[i].RetencionIvaId is not null);
                hallado = true;
                break;
            }
            if (!hallado) return false;
        }
        return true;
    }

    /// <summary>Índices de un subconjunto cuya suma cumple <paramref name="cumple"/>; prueba primero «todos».</summary>
    private static List<int>? SubconjuntoConSuma(List<decimal> montos, Func<decimal, bool> cumple)
    {
        if (montos.Count > 0 && cumple(montos.Sum())) return Enumerable.Range(0, montos.Count).ToList();
        var alcanzables = new Dictionary<decimal, List<int>> { [0m] = [] };
        for (var i = 0; i < montos.Count; i++)
        {
            foreach (var (suma, indices) in alcanzables.ToList())
            {
                var nueva = suma + montos[i];
                if (!alcanzables.ContainsKey(nueva)) alcanzables[nueva] = [.. indices, i];
            }
            if (alcanzables.Count > 200_000) break;
        }
        return alcanzables.Where(x => x.Value.Count > 0 && cumple(x.Key)).Select(x => x.Value).FirstOrDefault();
    }
}
