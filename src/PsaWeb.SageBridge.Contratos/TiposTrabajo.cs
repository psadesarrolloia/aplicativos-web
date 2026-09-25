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

    /// <summary>
    /// Crea o actualiza (en el lugar, un solo guardado) la Purchase Order de una factura de compra, con su proveedor y su
    /// numeración (§6.1 del plan). Payload <see cref="PayloadGuardarOc"/>, resultado <see cref="ResultadoGuardarOc"/>.
    /// </summary>
    public const string GuardarOc = "GuardarOc";

    public static IReadOnlyList<string> Todos { get; } = new[] { ProbarEmpresa, GuardarOc };

    /// <summary>Tipos que no escriben en Sage y por eso se procesan aunque la empresa no esté habilitada.</summary>
    public static bool PermitidoSinHabilitar(string tipo) => tipo == ProbarEmpresa;
}
