namespace PsaWeb.Conciliacion;

/// <summary>
/// Rango de fechas de emisión que acepta el WS público del SRI. Medido el 2026-09-19 contra
/// producción: solo el mes en curso y el mes anterior completo (todo lo emitido hasta el 31/07
/// fue rechazado con «fecha de emision fuera del rango permitido», todo desde el 01/08 aceptado).
/// </summary>
public static class RangoConsultaSri
{
    /// <summary>Primer día del mes anterior: desde ahí el WS acepta consultas.</summary>
    public static DateTime Inicio(DateTime hoy) => new DateTime(hoy.Year, hoy.Month, 1).AddMonths(-1);

    public static bool Contiene(DateTime fechaEmision, DateTime hoy) => fechaEmision.Date >= Inicio(hoy);
}
