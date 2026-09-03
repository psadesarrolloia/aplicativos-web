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

    /// <summary>Configuración del proceso automático en segundo plano.</summary>
    public WorkerOptions Worker { get; set; } = new();

    public sealed class WorkerOptions
    {
        /// <summary>
        /// Si es <c>true</c>, el Host corre <c>ProcesadorRetenciones</c> en bucle.
        /// Por defecto <c>false</c>: hay que habilitarlo explícitamente
        /// (<c>Retenciones:Worker:Habilitado=true</c>).
        /// </summary>
        public bool Habilitado { get; set; }

        /// <summary>Tiempo entre corridas. Por defecto 60 minutos; mínimo efectivo 1 minuto.</summary>
        public TimeSpan Intervalo { get; set; } = TimeSpan.FromMinutes(60);

        /// <summary>Espera antes de la primera corrida (deja arrancar el Host). Por defecto 2 minutos.</summary>
        public TimeSpan RetrasoInicial { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>Nombre que queda en <c>DatilRequests.User</c> para las corridas automáticas.</summary>
        public string Usuario { get; set; } = "worker";
    }
}
