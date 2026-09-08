namespace PsaWeb.Modules.FacturacionElectronica;

public sealed class FacturacionElectronicaOptions
{
    public const string SectionName = "FacturacionElectronica";

    /// <summary>Código de forma de pago de Datil para el pago al contado de las facturas (default "20" = otros).</summary>
    public string FormaPagoContado { get; set; } = "20";

    /// <summary>Si es true, el módulo usa datos de muestra (no toca Sage 50 ni Datil real).</summary>
    public bool UsarMuestra { get; set; }
}
