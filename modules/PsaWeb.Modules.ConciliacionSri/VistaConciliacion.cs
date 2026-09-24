using PsaWeb.Comprobantes.Compras;
using PsaWeb.Conciliacion;

namespace PsaWeb.Modules.ConciliacionSri;

/// <summary>Una fila de la conciliación con su revisión manual ("Aceptada / Revisada OK"), si tiene.</summary>
public sealed record FilaConRevision(FilaConciliacion Fila, RevisionDeFila? Revision)
{
    public EstadoRevision EstadoRevision => Revision?.Estado ?? EstadoRevision.SinRevisar;

    /// <summary>Nº de documento: el de Sage si la compra está registrada; si no (Solo en SRI), la serie del SRI.</summary>
    public string Documento => Fila.Sage?.Referencia ?? Fila.Sri?.SerieComprobante ?? string.Empty;

    public TipoDocumentoRecibido Tipo => Fila.Tipo;

    public string TipoTexto => TiposDocumentoRecibido.Etiqueta(Tipo);

    /// <summary>El SRI no trae montos de las retenciones: no hay total del SRI que mostrar ni comparar.</summary>
    public bool SriTieneMontos => Fila.Sri?.TieneMontos ?? true;

    public string ProveedorSri => Fila.Sri?.RazonSocialEmisor ?? string.Empty;

    public string ProveedorSage => Fila.Sage?.NombreProveedor ?? string.Empty;

    /// <summary>Solo las categorías con algo que revisar se pueden aceptar.</summary>
    public bool Aceptable => Fila.Clasificacion != ClasificacionConciliacion.CoincidePendienteDeVerificar;
}

public enum ColumnaConciliacion
{
    Tipo, Documento, ProveedorSri, ProveedorSage, TotalSri, TotalSage, EstadoSri, Revision,
}

/// <summary>
/// Filtros, búsqueda y orden de los listados. Cada campo de texto vacío = sin filtro. La clave de
/// acceso queda fuera a propósito (no se busca ni se ordena por ella).
/// </summary>
public sealed record CriteriosVista(
    string Busqueda = "",
    string Documento = "",
    string ProveedorSri = "",
    string ProveedorSage = "",
    string Diferencias = "",
    string EstadoSri = "",
    string Revision = "",
    string Tipo = "",
    ColumnaConciliacion? OrdenColumna = null,
    bool OrdenAscendente = true)
{
    public const string EstadoSinVerificar = "sin";
    public const string EstadoAutorizado = "aut";
    public const string EstadoNoVigente = "novigente";
    public const string EstadoOtro = "otro";
    public const string RevisionPendientes = "pend";
    public const string RevisionRevisadas = "ok";

    public bool HayFiltros =>
        Busqueda.Length > 0 || Documento.Length > 0 || ProveedorSri.Length > 0 || ProveedorSage.Length > 0
        || Diferencias.Length > 0 || EstadoSri.Length > 0 || Revision.Length > 0 || Tipo.Length > 0;
}

public static class VistaConciliacion
{
    public static IReadOnlyList<FilaConRevision> Aplicar(IEnumerable<FilaConRevision> filas, CriteriosVista c)
    {
        var q = filas;

        if (c.Busqueda.Length > 0)
        {
            q = q.Where(f => Contiene(f.Documento, c.Busqueda)
                || Contiene(f.ProveedorSri, c.Busqueda)
                || Contiene(f.ProveedorSage, c.Busqueda)
                || Contiene(f.Fila.Sri?.RucEmisor, c.Busqueda)
                || Contiene(TextoDiferencias(f), c.Busqueda)
                || Contiene(f.Revision?.Comentario, c.Busqueda));
        }

        // Tipo: el valor es el nombre del enum (lo que llena el <select> de la página).
        if (Enum.TryParse<TipoDocumentoRecibido>(c.Tipo, out var tipo)) q = q.Where(f => f.Tipo == tipo);
        if (c.Documento.Length > 0) q = q.Where(f => Contiene(f.Documento, c.Documento));
        if (c.ProveedorSri.Length > 0) q = q.Where(f => Contiene(f.ProveedorSri, c.ProveedorSri));
        if (c.ProveedorSage.Length > 0) q = q.Where(f => Contiene(f.ProveedorSage, c.ProveedorSage));
        if (c.Diferencias.Length > 0) q = q.Where(f => Contiene(TextoDiferencias(f), c.Diferencias));

        q = c.EstadoSri switch
        {
            CriteriosVista.EstadoSinVerificar => q.Where(f => string.IsNullOrEmpty(f.Fila.Sri?.Estado)),
            CriteriosVista.EstadoAutorizado => q.Where(f => f.Fila.Sri?.Estado == nameof(EstadoComprobanteSri.Autorizado)),
            CriteriosVista.EstadoNoVigente => q.Where(f => EstadoSriPresentacion.NoVigente(f.Fila.Sri?.Estado)),
            CriteriosVista.EstadoOtro => q.Where(f => f.Fila.Sri?.Estado is { Length: > 0 } e
                && e != nameof(EstadoComprobanteSri.Autorizado) && !EstadoSriPresentacion.NoVigente(e)),
            _ => q,
        };

        q = c.Revision switch
        {
            CriteriosVista.RevisionPendientes => q.Where(f => f.Aceptable && f.EstadoRevision != EstadoRevision.Revisada),
            CriteriosVista.RevisionRevisadas => q.Where(f => f.EstadoRevision == EstadoRevision.Revisada),
            _ => q,
        };

        if (c.OrdenColumna is { } columna)
        {
            q = columna switch
            {
                ColumnaConciliacion.Tipo => Ordenar(q, f => f.TipoTexto, c.OrdenAscendente),
                ColumnaConciliacion.Documento => Ordenar(q, f => f.Documento, c.OrdenAscendente),
                ColumnaConciliacion.ProveedorSri => Ordenar(q, f => f.ProveedorSri, c.OrdenAscendente),
                ColumnaConciliacion.ProveedorSage => Ordenar(q, f => f.ProveedorSage, c.OrdenAscendente),
                ColumnaConciliacion.TotalSri => Ordenar(q, f => f.Fila.Sri?.Total, c.OrdenAscendente),
                ColumnaConciliacion.TotalSage => Ordenar(q, f => f.Fila.Sage?.Total, c.OrdenAscendente),
                ColumnaConciliacion.EstadoSri => Ordenar(q, f => EstadoSriPresentacion.Visual(f.Fila.Sri?.Estado).Texto, c.OrdenAscendente),
                ColumnaConciliacion.Revision => Ordenar(q, f => (int)f.EstadoRevision, c.OrdenAscendente),
                _ => q,
            };
        }

        return q.ToList();
    }

    public static string TextoDiferencias(FilaConRevision f) => string.Join(" ", f.Fila.Diferencias);

    /// <summary>
    /// Cuánto le sobra (+) o le falta (−) a Sage respecto del SRI en un campo de monto ("Subtotal", "IVA", "Total"),
    /// para ubicar la línea que explica la diferencia. Null si el campo no es un monto o falta uno de los dos lados.
    /// Se calcula al mostrar y no forma parte del texto de <see cref="FilaConciliacion.Diferencias"/>: ese texto entra
    /// en la huella de las revisiones ya guardadas y cambiarlo las dejaría "desactualizadas".
    /// </summary>
    public static decimal? ValorDiferencia(FilaConciliacion fila, string campo) =>
        fila.Sri is null || fila.Sage is null
            ? null
            : campo switch
            {
                "Subtotal" => fila.Sage.Subtotal - fila.Sri.Subtotal,
                "IVA" => fila.Sage.Iva - fila.Sri.Iva,
                "Total" => fila.Sage.Total - fila.Sri.Total,
                _ => null,
            };

    private static bool Contiene(string? texto, string buscado) =>
        texto is not null && texto.Contains(buscado, StringComparison.OrdinalIgnoreCase);

    // OrderBy es estable: filas con la misma clave conservan el orden original de la conciliación.
    private static IEnumerable<FilaConRevision> Ordenar<TClave>(
        IEnumerable<FilaConRevision> q, Func<FilaConRevision, TClave> clave, bool ascendente) =>
        ascendente ? q.OrderBy(clave) : q.OrderByDescending(clave);
}
