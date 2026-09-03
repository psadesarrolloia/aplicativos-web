namespace PsaWeb.Datil;

public sealed class DatilOptions
{
    public const string SectionName = "Datil";

    /// <summary>
    /// Si es true, <see cref="IDatilClient.EmitirRetencionAsync"/> arma y valida el
    /// comprobante pero NO llama al API. Por defecto <c>true</c>: enviar de verdad
    /// hay que habilitarlo explícitamente (config <c>Datil:DryRun=false</c>).
    /// Enviar a Datil autoriza el comprobante ante el SRI — es irreversible.
    /// </summary>
    public bool DryRun { get; set; } = true;
}
