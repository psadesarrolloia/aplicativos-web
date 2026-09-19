namespace PsaWeb.Conciliacion;

/// <summary>
/// Cómo se muestra el estado en el SRI en cualquier listado (Conciliación SRI y los 4
/// comprobantes electrónicos): mismo texto y mismo color en todos lados.
/// </summary>
public static class EstadoSriPresentacion
{
    public static (string Texto, string Clase) Visual(string? estado) => estado switch
    {
        null or "" => ("Sin verificar", "psa-estado--warn"),
        nameof(EstadoComprobanteSri.Autorizado) => ("Autorizado", "psa-estado--ok"),
        nameof(EstadoComprobanteSri.Anulado) => ("ANULADO", "psa-estado--bad"),
        nameof(EstadoComprobanteSri.NoAutorizado) => ("NO AUTORIZADO", "psa-estado--bad"),
        nameof(EstadoComprobanteSri.FormatoInvalido) => ("Formato inválido", "psa-estado--warn"),
        nameof(EstadoComprobanteSri.NoEncontrado) => ("No encontrado", "psa-estado--warn"),
        nameof(EstadoComprobanteSri.FueraDeRango) => ("Fuera de rango del SRI", "psa-estado--warn"),
        nameof(EstadoComprobanteSri.Otro) => ("Otro estado (ver detalle)", "psa-estado--warn"),
        nameof(EstadoComprobanteSri.ErrorServicio) => ("Error del servicio", "psa-estado--warn"),
        _ => (estado, "psa-estado--warn"),
    };

    /// <summary>true si el estado indica que el comprobante ya no está vigente en el SRI.</summary>
    public static bool NoVigente(string? estado) =>
        estado is nameof(EstadoComprobanteSri.Anulado) or nameof(EstadoComprobanteSri.NoAutorizado);
}
