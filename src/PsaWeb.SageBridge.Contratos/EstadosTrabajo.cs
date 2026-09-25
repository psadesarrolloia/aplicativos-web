namespace PsaWeb.SageBridge.Contratos;

/// <summary>Estados de un trabajo de la cola (<c>TrabajosSage.Estado</c>).</summary>
public static class EstadosTrabajo
{
    /// <summary>Esperando al Bridge (o a <c>NoAntesDeUtc</c> si es un reintento diferido).</summary>
    public const string EnCola = "EnCola";

    /// <summary>Tomado por el Bridge hasta <c>TomadoHastaUtc</c>; vencido ese plazo, otro ciclo puede retomarlo.</summary>
    public const string EnProceso = "EnProceso";

    public const string Hecho = "Hecho";
    public const string Error = "Error";
    public const string Cancelado = "Cancelado";

    public static bool EsFinal(string estado) => estado == Hecho || estado == Error || estado == Cancelado;
}
