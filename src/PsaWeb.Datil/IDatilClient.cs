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

    /// <summary>Consulta el estado de un comprobante ya emitido. Devuelve el campo <c>estado</c>.</summary>
    Task<string?> ConsultarEstadoAsync(
        string id, DatilCredentials credenciales, CancellationToken cancellationToken = default);
}
