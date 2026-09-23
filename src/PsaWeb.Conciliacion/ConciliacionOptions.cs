namespace PsaWeb.Conciliacion;

public sealed class ConciliacionOptions
{
    public const string SectionName = "Conciliacion";

    /// <summary>
    /// URL del WS <c>ConsultaComprobante</c> (§4.5/§14.1 del plan) — por defecto
    /// producción. En pruebas: <c>https://celcer.sri.gob.ec/comprobantes-electronicos-ws/ConsultaComprobante</c>.
    /// </summary>
    public string UrlConsultaComprobante { get; set; } = "https://cel.sri.gob.ec/comprobantes-electronicos-ws/ConsultaComprobante";

    /// <summary>No volver a verificar una fila si ya se verificó hace menos de este umbral.</summary>
    public int VerificacionUmbralDias { get; set; } = 7;

    /// <summary>Máximo de llamadas simultáneas al WS del SRI durante una corrida (worker o botón masivo).</summary>
    public int VerificacionConcurrenciaMaxima { get; set; } = 5;

    /// <summary>
    /// Días que el SRI da para emitir una retención después de la venta. En retenciones, el comprobante del SRI y su
    /// registro en Sage pueden diferir en la fecha hasta esta cantidad de días: no se marca como diferencia, se busca
    /// la pareja en una ventana de ±este valor alrededor del período, y una retención sin pareja que todavía está
    /// dentro de este plazo se muestra como "en plazo" (puede simplemente no haberse emitido/registrado aún).
    /// </summary>
    public int RetencionPlazoDias { get; set; } = 5;

    /// <summary>Configuración del worker de verificación de estado (§13.4/§14.2).</summary>
    public WorkerOptions Worker { get; set; } = new();

    public sealed class WorkerOptions
    {
        /// <summary>
        /// A diferencia de <c>RetencionesWorker</c>, arranca en <c>true</c>: este
        /// worker no emite nada ni escribe en Sage/Datil, solo lee un WS público
        /// y actualiza 2 columnas propias — el peor caso es que falle y
        /// reintente, sin nada que revertir (§13.4, decidido 2026-09-17).
        /// </summary>
        public bool Habilitado { get; set; } = true;

        /// <summary>Tiempo entre corridas. Por defecto 8 horas.</summary>
        public TimeSpan Intervalo { get; set; } = TimeSpan.FromHours(8);

        /// <summary>Espera antes de la primera corrida (deja arrancar el Host). Por defecto 2 minutos.</summary>
        public TimeSpan RetrasoInicial { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>Ventana móvil de <c>FechaEmision</c> a considerar en cada corrida. Por defecto 90 días, pero nunca más atrás de lo que acepta el WS del SRI (<see cref="RangoConsultaSri"/>).</summary>
        public int VentanaDias { get; set; } = 90;
    }
}
