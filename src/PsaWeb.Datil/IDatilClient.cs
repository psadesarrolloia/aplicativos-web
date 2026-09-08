using PsaWeb.Datil.Model;

namespace PsaWeb.Datil;

public interface IDatilClient
{
    /// <summary>
    /// Emite una retención en Datil. En modo <c>DryRun</c> serializa y valida el
    /// comprobante pero no llama al API.
    /// </summary>
    Task<DatilEmisionResult> EmitirRetencionAsync(
        Retencion retencion, DatilCredentials credenciales, CancellationToken cancellationToken = default);

    /// <summary>Emite una factura de venta. Respeta <c>DryRun</c>.</summary>
    Task<DatilEmisionResult> EmitirFacturaAsync(
        Factura factura, DatilCredentials credenciales, CancellationToken cancellationToken = default);

    /// <summary>Emite una nota de crédito de venta. Respeta <c>DryRun</c>.</summary>
    Task<DatilEmisionResult> EmitirNotaCreditoAsync(
        NotaCredito notaCredito, DatilCredentials credenciales, CancellationToken cancellationToken = default);

    /// <summary>Emite una liquidación de compra. Respeta <c>DryRun</c>.</summary>
    Task<DatilEmisionResult> EmitirLiquidacionAsync(
        Liquidacion liquidacion, DatilCredentials credenciales, CancellationToken cancellationToken = default);

    /// <summary>Consulta el estado de un comprobante ya emitido. Devuelve el campo <c>estado</c>.</summary>
    Task<string?> ConsultarEstadoAsync(
        string id, DatilCredentials credenciales, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta un comprobante ya emitido y devuelve una descripción para mostrar
    /// (estado, o primer error, o "sin respuesta"). Port de <c>QueryToDatil</c>.
    /// </summary>
    Task<DatilConsultaResult> ConsultarComprobanteAsync(
        string id, DatilCredentials credenciales, CancellationToken cancellationToken = default);
}
