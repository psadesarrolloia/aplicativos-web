namespace PsaWeb.Modules.Retenciones;

public sealed class RetencionesOptions
{
    public const string SectionName = "Retenciones";

    /// <summary>RUCs de empresas activas que igual se saltan (lista de omisión del <c>Program.cs</c> original).</summary>
    public List<string> OmitirRucs { get; set; } = new();

    /// <summary>
    /// Si se define, fuerza el ambiente (1 pruebas / 2 producción) para todas las
    /// empresas. Si no, se usa <c>CurrentAmbient.AmbientDefault</c> de cada una
    /// (y 2 si no hay registro).
    /// </summary>
    public short? AmbienteForzado { get; set; }
}
