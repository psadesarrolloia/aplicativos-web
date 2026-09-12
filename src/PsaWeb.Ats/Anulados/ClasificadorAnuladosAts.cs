using PsaWeb.Ats.Esquema;

namespace PsaWeb.Ats.Anulados;

/// <summary>
/// Arma los <c>detalleAnuladosType</c> de un comprobante cancelado. Parte
/// <b>pura</b> (sin ODBC) de <c>LoadCanceled</c> (<c>ATSfromPeach</c>) — la
/// parte más intrincada del ATS: según el diario puede no aportar ninguna
/// fila, una, o dos (liquidación + retención sobre el mismo documento).
/// </summary>
public static class ClasificadorAnuladosAts
{
    public const string TipoFactura = "18";
    public const string TipoNotaCredito = "04";
    public const string TipoLiquidacion = "03";
    public const string TipoRetencion = "07";

    /// <summary>Relleno cuando no hay línea AUT-SRI (o la de la orden vinculada) para el comprobante.</summary>
    public const string AutorizacionDeRelleno = "9999999999";

    /// <param name="ordenesDeCompraCanceladas">
    /// Filas de <c>JrnlHdr</c> (diario 10, <c>JournalEx</c> 18) que comparten
    /// período con al menos un comprobante de compra cancelado — el `.exe`
    /// las trae de una sola consulta para todo el lote, acá se pasan ya
    /// resueltas para poder testear la clasificación sin ODBC.
    /// </param>
    /// <param name="autorizacionesPorPostOrder">
    /// <c>RowDescription</c> del ítem <c>AUT-SRI</c> por <c>PostOrder</c>
    /// (primero que aparezca, igual que el <c>FirstOrDefault()</c> original).
    /// </param>
    public static IReadOnlyList<detalleAnuladosType> Clasificar(
        FilaJrnlHdrAts fila,
        IReadOnlyList<FilaJrnlHdrAts> ordenesDeCompraCanceladas,
        IReadOnlyDictionary<long, string> autorizacionesPorPostOrder)
    {
        var resultado = new List<detalleAnuladosType>();
        var diario = ClasificadorDiarioAnuladoAts.ClasificarDiario(fila.JrnlKeyJournal);
        var subtipo = ClasificadorDiarioAnuladoAts.ClasificarSubtipo(fila.JournalEx);

        detalleAnuladosType? porReferenciaPropia = null;

        switch (diario)
        {
            case DiarioAnuladoAts.Sales:
                porReferenciaPropia = subtipo switch
                {
                    SubtipoAnuladoAts.SaleInvoice => new detalleAnuladosType { tipoComprobante = TipoFactura },
                    SubtipoAnuladoAts.SaleCreditMemo => new detalleAnuladosType { tipoComprobante = TipoNotaCredito },
                    _ => null,
                };
                break;

            case DiarioAnuladoAts.PurchaseReceiveInventory:
                if (subtipo == SubtipoAnuladoAts.PurchaseReceiveInventory && fila.InvPosoOrderNumber.Length > 0)
                {
                    // La compra original (factura/NV/liq) referenciada por este
                    // comprobante cancelado: mismo INV_POSOOrderNumber + proveedor.
                    var compraOriginal = ordenesDeCompraCanceladas.FirstOrDefault(
                        x => x.Reference == fila.InvPosoOrderNumber && x.CustVendId == fila.CustVendId);

                    if (compraOriginal is not null)
                    {
                        if (compraOriginal.ShipVia == "LIQUIDACION")
                        {
                            var liq = ArmarDesdeReferencia(TipoLiquidacion, compraOriginal.TermsDescription);
                            if (liq is not null)
                            {
                                liq.autorizacion = Autorizacion(autorizacionesPorPostOrder, compraOriginal.PostOrder) ?? AutorizacionDeRelleno;
                                resultado.Add(liq);
                            }
                        }

                        if (compraOriginal.ShipToAddress2.Length > 0)
                        {
                            var ret = ArmarDesdeReferencia(TipoRetencion, compraOriginal.ShipToAddress2);
                            if (ret is not null)
                            {
                                ret.autorizacion = Autorizacion(autorizacionesPorPostOrder, compraOriginal.PostOrder) ?? AutorizacionDeRelleno;
                                resultado.Add(ret);
                            }
                        }
                    }
                }

                // Ya se agregó (0, 1 o 2 veces) arriba — no pasa por la cola común.
                porReferenciaPropia = null;
                break;

            case DiarioAnuladoAts.PurchaseOrder:
                if (subtipo == SubtipoAnuladoAts.PurchaseOrder)
                {
                    // Si esta orden de compra está referenciada como origen de
                    // otro documento, no es ella la que se reporta como anulada
                    // (el `.exe`: "JrnlHdrRelated.Count() == 0").
                    var estaReferenciada = ordenesDeCompraCanceladas.Any(x => EsNumero(x.InvPosoOrderNumber, fila.PostOrder));
                    if (!estaReferenciada)
                    {
                        if (fila.ShipVia == "LIQUIDACION")
                        {
                            var liq = ArmarDesdeReferencia(TipoLiquidacion, fila.TermsDescription);
                            if (liq is not null)
                            {
                                // Sin relleno "9999999999" acá — asimetría real
                                // del `.exe` (las otras 3 ramas similares sí lo
                                // tienen), preservada tal cual.
                                liq.autorizacion = Autorizacion(autorizacionesPorPostOrder, fila.PostOrder);
                                resultado.Add(liq);
                            }
                        }

                        // La retención se arma siempre (sin chequear longitud
                        // de ShipToAddress2 en el `.exe` — acá sí, para no
                        // reventar con un ArgumentOutOfRangeException si viene
                        // corto; ver ArmarDesdeReferencia) y fluye a la cola
                        // común de abajo.
                        porReferenciaPropia = ArmarDesdeReferencia(TipoRetencion, fila.ShipToAddress2);
                    }
                }

                break;
        }

        if (porReferenciaPropia is not null)
        {
            if (porReferenciaPropia.tipoComprobante != TipoRetencion)
            {
                var partes = ArmarDesdeReferencia(porReferenciaPropia.tipoComprobante!, fila.Reference);
                if (partes is null)
                {
                    return resultado; // Reference demasiado corta — no debería pasar (ya filtrada por SQL), pero por las dudas.
                }

                porReferenciaPropia.establecimiento = partes.establecimiento;
                porReferenciaPropia.puntoEmision = partes.puntoEmision;
                porReferenciaPropia.secuencialInicio = partes.secuencialInicio;
            }

            porReferenciaPropia.secuencialFin = porReferenciaPropia.secuencialInicio;
            porReferenciaPropia.autorizacion = Autorizacion(autorizacionesPorPostOrder, fila.PostOrder) ?? AutorizacionDeRelleno;
            resultado.Add(porReferenciaPropia);
        }

        return resultado;
    }

    private static string? Autorizacion(IReadOnlyDictionary<long, string> autorizaciones, long postOrder) =>
        autorizaciones.TryGetValue(postOrder, out var valor) ? valor : null;

    private static bool EsNumero(string texto, long valor) => long.TryParse(texto, out var n) && n == valor;

    /// <summary>
    /// Arma un <c>detalleAnuladosType</c> a partir de un texto
    /// "<c>est-pto-secuencial</c>" (<c>Reference</c>, <c>TermsDescription</c> o
    /// <c>ShipToAddress2</c> según el caso). Devuelve <c>null</c> si el texto es
    /// demasiado corto — el `.exe` no se cuida de esto y tiraría
    /// <see cref="ArgumentOutOfRangeException"/>; acá se prefiere omitir la
    /// fila a interrumpir toda la carga del ATS.
    /// </summary>
    private static detalleAnuladosType? ArmarDesdeReferencia(string tipoComprobante, string referencia)
    {
        if (referencia.Length < 8)
        {
            return null;
        }

        return new detalleAnuladosType
        {
            tipoComprobante = tipoComprobante,
            establecimiento = referencia[..3],
            puntoEmision = referencia.Substring(4, 3),
            secuencialInicio = referencia[8..],
        };
    }
}
