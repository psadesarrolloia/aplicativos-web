using PsaWeb.Modules.ComprobantesElectronicos.Retenciones;

namespace PsaWeb.Modules.ComprobantesElectronicos;

/// <summary>Un comprobante pendiente de generar: identificador en Sage 50, número de referencia y fecha.</summary>
public sealed record PendienteComprobante(string PostOrder, string Numero, DateTime Fecha);

/// <summary>
/// Fachada única de generación para los 4 tipos: la interfaz llama siempre a estos
/// métodos y el tipo decide el procesador (ventas → <see cref="ProcesadorComprobantesVenta"/>,
/// retenciones → <see cref="ProcesadorRetenciones"/> detrás del candado single-flight
/// compartido con el worker de retenciones).
/// </summary>
public sealed class ServicioComprobantes(
    ProcesadorComprobantesVenta venta,
    ProcesadorRetenciones retenciones,
    EjecucionRetencionesGate gate)
{
    public bool DryRun => venta.DryRun;

    public async Task<IReadOnlyList<PendienteComprobante>> ListarPendientesAsync(
        TipoComprobante tipo, string ruc, short ambiente, DateTime desde, DateTime hasta,
        CancellationToken ct = default)
    {
        switch (tipo)
        {
            case TipoComprobante.Factura:
                return (await venta.ListarFacturasPendientesAsync(ruc, desde, hasta, ct))
                    .Select(p => new PendienteComprobante(p.PostOrder, p.NumeroReferencia, p.Fecha)).ToList();
            case TipoComprobante.NotaCredito:
                return (await venta.ListarNotasCreditoPendientesAsync(ruc, desde, hasta, ct))
                    .Select(p => new PendienteComprobante(p.PostOrder, p.NumeroReferencia, p.Fecha)).ToList();
            case TipoComprobante.Liquidacion:
                return (await venta.ListarLiquidacionesPendientesAsync(ruc, desde, hasta, ct))
                    .Select(p => new PendienteComprobante(p.PostOrder, p.NumeroReferencia, p.Fecha)).ToList();
            default:
                return (await retenciones.ListarPendientesAsync(ruc, ambiente, desde, hasta, ct))
                    .Select(p => new PendienteComprobante(p.PostOrder, p.NumeroReferencia, p.Fecha)).ToList();
        }
    }

    public async Task<ResultadoComprobante> ProcesarUnoAsync(
        TipoComprobante tipo, string ruc, string postOrder, short ambiente, string usuario,
        CancellationToken ct = default)
    {
        switch (tipo)
        {
            case TipoComprobante.Factura:
                return await venta.ProcesarFacturaAsync(ruc, postOrder, ambiente, usuario, ct);
            case TipoComprobante.NotaCredito:
                return await venta.ProcesarNotaCreditoAsync(ruc, postOrder, ambiente, usuario, ct);
            case TipoComprobante.Liquidacion:
                return await venta.ProcesarLiquidacionAsync(ruc, postOrder, ambiente, usuario, ct);
            default:
                var (ejecuto, valor, motivo) = await gate.IntentarAsync(usuario,
                    c => retenciones.ProcesarUnaCompraAsync(ruc, ambiente, usuario, postOrder, c), ct);
                return ejecuto ? valor! : ResultadoComprobante.Error(postOrder, string.Empty, motivo ?? "Ocupado.");
        }
    }

    public async Task<ResumenLote> ProcesarLoteAsync(
        TipoComprobante tipo, string ruc, DateTime desde, DateTime hasta, short ambiente, string usuario,
        CancellationToken ct = default)
    {
        switch (tipo)
        {
            case TipoComprobante.Factura:
                return await venta.ProcesarLoteFacturasAsync(ruc, desde, hasta, ambiente, usuario, ct);
            case TipoComprobante.NotaCredito:
                return await venta.ProcesarLoteNotasCreditoAsync(ruc, desde, hasta, ambiente, usuario, ct);
            case TipoComprobante.Liquidacion:
                return await venta.ProcesarLoteLiquidacionesAsync(ruc, desde, hasta, ambiente, usuario, ct);
            default:
                var inicio = DateTimeOffset.Now;
                var (ejecuto, valor, motivo) = await gate.IntentarAsync(usuario,
                    c => retenciones.ProcesarLoteAsync(ruc, ambiente, desde, hasta, usuario, c), ct);
                return ejecuto
                    ? valor!
                    : new ResumenLote("Retenciones", inicio, DateTimeOffset.Now, DryRun,
                        new[] { ResultadoComprobante.Error(string.Empty, string.Empty, motivo ?? "Ocupado.") });
        }
    }
}
