namespace PsaWeb.Conciliacion.Tests;

/// <summary>El WS del SRI solo responde por el mes en curso y el mes anterior (medido en producción el 2026-09-19).</summary>
public class RangoConsultaSriTests
{
    [Theory]
    [InlineData("2026-09-19", "2026-08-01")]
    [InlineData("2026-10-01", "2026-09-01")]   // al cambiar de mes el rango se corre
    [InlineData("2026-01-15", "2025-12-01")]
    public void Inicio_es_el_primer_dia_del_mes_anterior(string hoy, string esperado)
        => Assert.Equal(DateTime.Parse(esperado), RangoConsultaSri.Inicio(DateTime.Parse(hoy)));

    [Theory]
    [InlineData("2026-07-31", false)]   // medido: rechazado
    [InlineData("2026-08-01", true)]    // medido: aceptado
    [InlineData("2026-09-19", true)]
    public void Frontera_medida_en_produccion(string emision, bool dentro)
        => Assert.Equal(dentro, RangoConsultaSri.Contiene(DateTime.Parse(emision), new DateTime(2026, 9, 19)));
}
