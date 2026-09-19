using PsaWeb.Conciliacion.Data;

namespace PsaWeb.Conciliacion;

public enum EstadoRevision
{
    /// <summary>Nadie la revisó.</summary>
    SinRevisar,

    /// <summary>Un revisor la aceptó y el hallazgo sigue siendo el mismo.</summary>
    Revisada,

    /// <summary>Se revisó, pero el hallazgo cambió desde entonces: hay que revisarla de nuevo.</summary>
    Desactualizada,
}

/// <summary>Una revisión aplicada a una fila, tal como la guarda <see cref="RevisionConciliacion"/>.</summary>
public sealed record RevisionDeFila(string Comentario, string RevisadaPor, DateTime RevisadaUtc, EstadoRevision Estado);

/// <summary>
/// Identidad y huella de una fila de la conciliación, para las revisiones manuales
/// ("Aceptada / Revisada OK"). Lógica pura, sin EF.
/// </summary>
public static class ClaveRevision
{
    public const int MaxComentario = 500;

    /// <summary>
    /// Con comprobante del SRI la fila se identifica por su clave de acceso (única por RUC); las de
    /// "Solo en Sage" no tienen, así que van por el <c>PostOrder</c> de la compra en Sage.
    /// </summary>
    public static string De(FilaConciliacion fila) =>
        fila.Sri is not null ? $"C:{fila.Sri.ClaveAcceso}" : $"P:{fila.Sage!.PostOrder}";

    /// <summary>
    /// Clasificación + diferencias. Cambia si la fila cambia de categoría o si cambia alguno de los
    /// valores que difieren (los textos de las diferencias incluyen ambos montos).
    /// </summary>
    public static string Huella(FilaConciliacion fila) =>
        $"{fila.Clasificacion}|{string.Join('|', fila.Diferencias)}";

    public static EstadoRevision Evaluar(FilaConciliacion fila, RevisionConciliacion? revision)
    {
        if (revision is null)
        {
            return EstadoRevision.SinRevisar;
        }

        return revision.Huella.Length == 0 || revision.Huella == Huella(fila)
            ? EstadoRevision.Revisada
            : EstadoRevision.Desactualizada;
    }
}
