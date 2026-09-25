using System.ComponentModel.DataAnnotations;
using PsaWeb.SageBridge.Contratos;

namespace PsaWeb.SageBridge.Cola.Data;

/// <summary>
/// Un trabajo para el Sage Bridge (tabla <c>TrabajosSage</c>). La web lo encola; el Bridge lo toma con un
/// <i>lease</i> (<see cref="TomadoPor"/> / <see cref="TomadoHastaUtc"/>), lo ejecuta contra Sage y deja el
/// resultado. Los nombres de tabla y columnas los usa también el Bridge por SQL directo: no renombrar sin
/// cambiar <c>ColaSql</c> del Bridge.
/// </summary>
public class TrabajoSage
{
    public long Id { get; set; }

    /// <summary>RUC de la empresa (compañía de Sage) sobre la que se ejecuta.</summary>
    [MaxLength(13)]
    public string Ruc { get; set; } = string.Empty;

    /// <summary>Uno de <see cref="TiposTrabajo"/>.</summary>
    [MaxLength(40)]
    public string Tipo { get; set; } = string.Empty;

    /// <summary>Uno de <see cref="EstadosTrabajo"/>.</summary>
    [MaxLength(20)]
    public string Estado { get; set; } = EstadosTrabajo.EnCola;

    /// <summary>
    /// Clave única por (<see cref="Ruc"/>, <see cref="Tipo"/>): encolar dos veces lo mismo devuelve el trabajo
    /// existente en vez de duplicarlo (p. ej. <c>vendorRecordNumber|nº factura</c> para una OC).
    /// </summary>
    [MaxLength(200)]
    public string ClaveIdempotencia { get; set; } = string.Empty;

    /// <summary>Datos de entrada (JSON). Su forma depende de <see cref="Tipo"/>.</summary>
    public string? PayloadJson { get; set; }

    /// <summary>Resultado (JSON) cuando termina bien.</summary>
    public string? ResultadoJson { get; set; }

    /// <summary>Mensaje del último error (visible para el usuario).</summary>
    [MaxLength(2000)]
    public string? Error { get; set; }

    /// <summary>Veces que el Bridge lo tomó.</summary>
    public int Intentos { get; set; }

    /// <summary>Usuario web que lo encoló.</summary>
    [MaxLength(256)]
    public string CreadoPor { get; set; } = string.Empty;

    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Reintento diferido: el Bridge no lo toma antes de esta hora.</summary>
    public DateTime? NoAntesDeUtc { get; set; }

    /// <summary>Instancia del Bridge que lo tiene tomado.</summary>
    [MaxLength(100)]
    public string? TomadoPor { get; set; }

    /// <summary>Fin del lease: vencido, el trabajo se puede volver a tomar (el Bridge se cayó a mitad).</summary>
    public DateTime? TomadoHastaUtc { get; set; }

    public DateTime? IniciadoUtc { get; set; }

    public DateTime? TerminadoUtc { get; set; }
}
