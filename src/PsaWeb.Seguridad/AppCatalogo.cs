namespace PsaWeb.Seguridad;

/// <summary>
/// Categorías del menú superior — agrupan los módulos para que el menú
/// crezca de forma ordenada a medida que se migran más aplicativos.
/// <see cref="Orden"/> fija el orden en que aparecen en la barra.
/// </summary>
public static class Categorias
{
    public const string Caja = "Caja";
    public const string Cartera = "Cartera";
    public const string Bancos = "Bancos";
    public const string Impuestos = "Impuestos";
    public const string ComprobantesElectronicos = "Comprobantes Electrónicos";
    public const string Inventario = "Inventario";

    public static readonly IReadOnlyList<string> Orden = new[]
    {
        Caja, Cartera, Bancos, Impuestos, ComprobantesElectronicos, Inventario,
    };
}

/// <summary>Un módulo web del menú / dashboard.</summary>
public sealed record AppWeb(
    string Id,
    string Nombre,
    string Descripcion,
    string Icono,
    string Ruta,
    string Categoria,
    IReadOnlyList<string> PermisosQueLaHabilitan)
{
    /// <summary>Visible si el usuario tiene al menos uno de los permisos (o si la lista está vacía).</summary>
    public bool VisiblePara(ContextoDeUsuario ctx) =>
        PermisosQueLaHabilitan.Count == 0 || PermisosQueLaHabilitan.Any(ctx.Puede);
}

/// <summary>
/// Catálogo de módulos web. Por ahora en código; puede pasar a una tabla
/// (<c>AppRegistry</c> en <c>PsaWebPlataforma</c>) en F-Shell-4.
/// </summary>
public static class AppCatalogo
{
    public static readonly IReadOnlyList<AppWeb> Todas = new List<AppWeb>
    {
        new("cierre-de-caja", "Cierre de Caja",
            "Cobros contra ventas registradas en Sage 50.",
            "💵", "/cierre-de-caja", Categorias.Caja,
            Array.Empty<string>()), // sin código propio todavía: visible para cualquier empresa

        // Provisional (GateProvisional): la fila quats existe en allowAction
        // pero tiene 0 filas en adrAllowRol (no está asignada a ningún rol
        // todavía — verificado 2026-09-12). Mientras tanto visible para
        // cualquier empresa. Revertir a new[] { Permisos.VerAts } cuando el
        // área asigne el permiso a los roles pertinentes.
        new("ats", "ATS",
            "Genera el XML del Anexo Transaccional Simplificado (ATS) del SRI a partir de Sage 50.",
            "📑", "/ats", Categorias.Impuestos,
            Array.Empty<string>()),

        // Provisional (GateProvisional): sin código propio hasta que el área
        // cargue quconcsri en allowAction. Mientras tanto visible para
        // cualquier empresa. Revertir a new[] { Permisos.VerConciliacionSri }
        // cuando el área asigne el permiso a los roles pertinentes.
        new("conciliacion-sri", "Conciliación SRI",
            "Concilia los comprobantes electrónicos recibidos del SRI contra Sage 50.",
            "🔎", "/conciliacion-sri", Categorias.Impuestos,
            Array.Empty<string>()),

        new("fe-facturas", "Facturas de venta",
            "Genera y emite en Datil las facturas de venta de Sage 50.",
            "🧾", "/fe/facturas", Categorias.ComprobantesElectronicos,
            new[] { Permisos.VerFacturas, Permisos.HacerFactura, Permisos.HacerFacturasLote }),

        new("retenciones", "Retenciones",
            "Genera y emite en Datil las retenciones de compra pendientes.",
            "📄", "/fe/retenciones", Categorias.ComprobantesElectronicos,
            new[] { Permisos.VerRetenciones, Permisos.HacerRetencion, Permisos.HacerRetencionesLote }),

        // Requiere las filas de docs/sql/permisos-comprobantes-v2.sql en allowAction
        // (y asignadas a roles); sin ellas la app queda invisible para todos.
        new("fe-liquidaciones", "Liquidaciones de compra",
            "Genera y emite en Datil las liquidaciones de compra.",
            "📥", "/fe/liquidaciones", Categorias.ComprobantesElectronicos,
            new[] { Permisos.VerLiquidaciones, Permisos.HacerLiquidacion, Permisos.HacerLiquidacionesLote }),

        new("fe-notas-credito", "Notas de crédito",
            "Genera y emite en Datil las notas de crédito de venta.",
            "↩️", "/fe/notas-credito", Categorias.ComprobantesElectronicos,
            new[] { Permisos.VerNotasCredito, Permisos.HacerNotaCredito, Permisos.HacerNotasCreditoLote }),

        // Provisional (GateProvisional): sin código propio hasta que el área
        // cargue quKardex en allowAction. Mientras tanto visible para cualquier
        // empresa. Revertir a new[] { Permisos.VerKardex } cuando esté el código.
        new("kardex", "Kardex",
            "Kardex de inventarios de Sage 50 (solo lectura).",
            "📦", "/kardex", Categorias.Inventario,
            Array.Empty<string>()),
    };

    public static IEnumerable<AppWeb> Habilitadas(ContextoDeUsuario ctx) =>
        Todas.Where(a => a.VisiblePara(ctx));
}
