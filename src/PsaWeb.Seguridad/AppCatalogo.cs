namespace PsaWeb.Seguridad;

/// <summary>Un aplicativo web del menú / dashboard.</summary>
public sealed record AppWeb(
    string Id,
    string Nombre,
    string Descripcion,
    string Icono,
    string Ruta,
    IReadOnlyList<string> PermisosQueLaHabilitan)
{
    /// <summary>Visible si el usuario tiene al menos uno de los permisos (o si la lista está vacía).</summary>
    public bool VisiblePara(ContextoDeUsuario ctx) =>
        PermisosQueLaHabilitan.Count == 0 || PermisosQueLaHabilitan.Any(ctx.Puede);
}

/// <summary>
/// Catálogo de apps web. Por ahora en código; puede pasar a una tabla
/// (<c>AppRegistry</c> en <c>PsaWebPlataforma</c>) en F-Shell-4.
/// </summary>
public static class AppCatalogo
{
    public static readonly IReadOnlyList<AppWeb> Todas = new List<AppWeb>
    {
        new("cierre-de-caja", "Cierre de Caja",
            "Cobros contra ventas registradas en Sage 50.",
            "💵", "/cierre-de-caja",
            Array.Empty<string>()), // sin código propio todavía: visible para cualquier empresa

        new("retenciones", "Retenciones",
            "Genera y emite en Datil las retenciones de compra pendientes.",
            "📄", "/retenciones",
            new[] { Permisos.VerRetenciones, Permisos.HacerRetencion, Permisos.HacerRetencionesLote }),

        new("fe-facturas", "FE · Facturas",
            "Genera y emite en Datil las facturas de venta de Sage 50.",
            "🧾", "/fe/facturas",
            new[] { Permisos.VerFacturas, Permisos.HacerFactura, Permisos.HacerFacturasLote }),

        new("fe-notas-credito", "FE · Notas de crédito",
            "Genera y emite en Datil las notas de crédito de venta.",
            "↩️", "/fe/notas-credito",
            new[] { Permisos.VerNotasCredito, Permisos.HacerNotaCredito }),

        new("fe-liquidaciones", "FE · Liquidaciones",
            "Genera y emite en Datil las liquidaciones de compra.",
            "📥", "/fe/liquidaciones",
            new[] { Permisos.VerLiquidaciones, Permisos.HacerLiquidacion }),
    };

    public static IEnumerable<AppWeb> Habilitadas(ContextoDeUsuario ctx) =>
        Todas.Where(a => a.VisiblePara(ctx));
}
