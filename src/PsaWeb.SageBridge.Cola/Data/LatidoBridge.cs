using System.ComponentModel.DataAnnotations;

namespace PsaWeb.SageBridge.Cola.Data;

/// <summary>
/// Último latido de una instancia del Bridge (tabla <c>LatidosBridge</c>): el proceso trabajador la actualiza
/// en cada vuelta de su ciclo. La administración la usa para saber si el Bridge está vivo y qué hace.
/// </summary>
public class LatidoBridge
{
    /// <summary>Equipo + nombre de la instancia (p. ej. <c>SERWEBPSA01/principal</c>).</summary>
    [Key]
    [MaxLength(100)]
    public string Instancia { get; set; } = string.Empty;

    public DateTime UltimoLatidoUtc { get; set; }

    /// <summary>Arranque del proceso trabajador actual.</summary>
    public DateTime IniciadoUtc { get; set; }

    /// <summary>Cuenta de Windows con la que corre (la autorización de Sage va atada a ella).</summary>
    [MaxLength(100)]
    public string Usuario { get; set; } = string.Empty;

    [MaxLength(40)]
    public string VersionAnfitrion { get; set; } = string.Empty;

    [MaxLength(40)]
    public string VersionLogica { get; set; } = string.Empty;

    /// <summary>Qué está haciendo (texto libre: «esperando trabajos», «procesando 3 trabajos de …», …).</summary>
    [MaxLength(300)]
    public string Estado { get; set; } = string.Empty;

    /// <summary>RUC de la compañía abierta en este momento, si hay una.</summary>
    [MaxLength(13)]
    public string? EmpresaAbierta { get; set; }
}
