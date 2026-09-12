using PsaWeb.Ats.Esquema;

namespace PsaWeb.Ats.Compras;

/// <summary>
/// Arma el <c>detalleComprasType</c> del ATS a partir de una
/// <see cref="CompraCruda"/> ya leída de Sage 50. Parte <b>pura</b> (sin ODBC)
/// — port de la cola aritmética de <c>LoadPurchases</c> (umbral de forma de
/// pago, retención recibida en compras, bloque de NC).
/// </summary>
public static class ArmadorComprasAts
{
    /// <summary>
    /// Monto (USD) a partir del cual se informa forma de pago — fijo, tal
    /// cual el `.exe` (decisión 2026-09-12, docs/PLAN-APP3-ATS.md §1.4
    /// hallazgo 2: no se lo condiciona por fecha).
    /// </summary>
    public const decimal LimiteFormaDePago = 500m;

    public static detalleComprasType Armar(CompraCruda c)
    {
        var proveedor = c.Proveedor;

        var detalle = new detalleComprasType
        {
            codSustento = c.CodSustento,
            tipoComprobante = c.TipoComprobante,
            parteRel = parteRelType.NO,
            parteRelSpecified = true,
            fechaRegistro = c.FechaRegistro,
            fechaEmision = c.FechaEmision,
            establecimiento = c.Establecimiento,
            puntoEmision = c.PuntoEmision,
            secuencial = c.Secuencial,
            autorizacion = c.Autorizacion,
            baseNoGraIva = c.Buckets.BaseNoGraIva,
            baseImponible = c.Buckets.BaseImponible,
            baseImpGrav = c.Buckets.BaseImpGrav,
            baseImpExe = c.Buckets.BaseImpExe,
            montoIce = 0m,
            montoIva = c.Buckets.MontoIva,
            valRetBien10 = c.Buckets.ValRetBien10,
            valRetBien10Specified = true,
            valRetServ20 = c.Buckets.ValRetServ20,
            valRetServ20Specified = true,
            valorRetBienes = c.Buckets.ValorRetBienes,
            valRetServ50 = c.Buckets.ValRetServ50,
            valRetServ50Specified = true,
            valorRetServicios = c.Buckets.ValorRetServicios,
            valRetServ100 = c.Buckets.ValRetServ100,
            // Hallazgo 4 de docs/PLAN-APP3-ATS.md §1.4: campo nuevo del SRI
            // (2020) que el `.exe` no calcula — el DIMM lo trae en 0.00 en
            // cada compra real, así que se emite igual aunque no se calcule.
            valorRetencionNc = 0m,
            valorRetencionNcSpecified = true,
            totbasesImpReemb = 0m,
            totbasesImpReembSpecified = true,
        };

        // Si el proveedor no se encontró en Sage (0 filas), el `.exe` no
        // asigna tpIdProv/idProv/pagoExterior — quedan en su default (null).
        if (proveedor.Encontrado)
        {
            detalle.tpIdProv = proveedor.TipoIdentificacion!;
            detalle.idProv = proveedor.Identificacion;
            if (proveedor.TipoIdentificacion == TiposIdentificacionProveedorAts.Exterior)
            {
                detalle.tipoProv = proveedor.TipoProveedorExterior;
                detalle.denoProv = proveedor.Nombre;
            }

            detalle.pagoExterior = proveedor.PagoExterior;
            detalle.pagoExterior.tipoRegiSpecified = proveedor.PagoExterior.pagoLocExt == pagoLocExtType.Item02;
        }

        var totalCompra = detalle.baseNoGraIva + detalle.baseImponible + detalle.baseImpExe
            + detalle.baseImpGrav + detalle.montoIce + detalle.montoIva;

        if (c.TipoComprobante is TiposComprobanteComprasAts.Factura or TiposComprobanteComprasAts.NotaVenta or TiposComprobanteComprasAts.Liquidacion)
        {
            if (totalCompra >= LimiteFormaDePago)
            {
                detalle.formasDePago = new[] { "20" };
            }

            if (c.ShipToAddress2.Length > 8 && c.ShipToCity.Length > 0)
            {
                detalle.estabRetencion1 = c.ShipToAddress2[..3];
                detalle.ptoEmiRetencion1 = c.ShipToAddress2.Substring(4, 3);
                detalle.secRetencion1 = c.ShipToAddress2[8..];
                detalle.autRetencion1 = c.ShipToCity;
                detalle.fechaEmiRet1 = detalle.fechaEmision;
            }

            detalle.air = c.RetencionesRenta;
        }
        else if (c.TipoComprobante == TiposComprobanteComprasAts.NotaCredito)
        {
            if (!string.IsNullOrEmpty(c.NumeroCompletoCompraOriginal))
            {
                var numero = c.NumeroCompletoCompraOriginal;
                detalle.docModificado = "01";
                detalle.estabModificado = numero[..3];
                detalle.ptoEmiModificado = numero.Substring(4, 3);
                detalle.secModificado = numero[8..];
                detalle.autModificado = c.AutorizacionCompraOriginal;
            }
        }

        return detalle;
    }
}
