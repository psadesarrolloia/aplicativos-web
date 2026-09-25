using System.ComponentModel.DataAnnotations;

namespace PsaWeb.SageBridge.Cola.Data;

/// <summary>
/// Configuración del Bridge por empresa (tabla <c>EmpresasBridge</c>). Sin fila = empresa no habilitada y
/// ventana de mantenimiento por defecto. Solo <c>ProbarEmpresa</c> se procesa en una empresa no habilitada.
/// </summary>
public class EmpresaBridge
{
    [Key]
    [MaxLength(13)]
    public string Ruc { get; set; } = string.Empty;

    /// <summary>¿El Bridge puede escribir en esta empresa? Se habilita tras aprobar el acceso en Sage.</summary>
    public bool Habilitada { get; set; }

    /// <summary>Ventana de mantenimiento <c>"HH:mm-HH:mm"</c>. Nula = la ventana por defecto del Bridge.</summary>
    [MaxLength(11)]
    public string? Ventana { get; set; }

    /// <summary>Último resultado de <c>VerifyAccess</c> que vio el Bridge (Granted, Pending, NoCredentials…).</summary>
    [MaxLength(30)]
    public string? AccesoSage { get; set; }

    public DateTime? AccesoVerificadoUtc { get; set; }

    [MaxLength(300)]
    public string? Nota { get; set; }

    [MaxLength(256)]
    public string ModificadoPor { get; set; } = string.Empty;

    public DateTime ModificadoUtc { get; set; } = DateTime.UtcNow;
}
