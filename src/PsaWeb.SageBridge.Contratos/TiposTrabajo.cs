using System.Collections.Generic;

namespace PsaWeb.SageBridge.Contratos;

/// <summary>
/// Tipos de trabajo que la web encola y el Bridge ejecuta contra Sage 50. Se guardan como texto en
/// <c>TrabajosSage.Tipo</c> (no como número) para que agregar un tipo no rompa filas viejas.
/// </summary>
public static class TiposTrabajo
{
    /// <summary>
    /// Abre y cierra la compañía por SDK sin escribir nada. Sirve para dar de alta una empresa en el Bridge:
    /// si Sage todavía no autorizó al Bridge, deja la solicitud pendiente para aprobarla en Sage.
    /// Se procesa aunque la empresa no esté habilitada.
    /// </summary>
    public const string ProbarEmpresa = "ProbarEmpresa";

    public static IReadOnlyList<string> Todos { get; } = new[] { ProbarEmpresa };

    /// <summary>Tipos que no escriben en Sage y por eso se procesan aunque la empresa no esté habilitada.</summary>
    public static bool PermitidoSinHabilitar(string tipo) => tipo == ProbarEmpresa;
}
