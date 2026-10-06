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
    public const string Compras = "Compras";
    public const string Ventas = "Ventas";
    public const string Bancos = "Bancos";
    public const string Impuestos = "Impuestos";
    public const string ComprobantesElectronicos = "Comprobantes Electrónicos Emitidos";
    public const string Inventario = "Inventario";

    public static readonly IReadOnlyList<string> Orden = new[]
    {
        Caja, Cartera, Compras, Ventas, Bancos, Impuestos, ComprobantesElectronicos, Inventario,
    };
}

/// <summary>Un módulo web del menú / dashboard.</summary>
/// <param name="PermisosQueLaHabilitan">Llaves reales del módulo: alcanza con una. Con la fuente de accesos <c>Web</c> son las únicas que valen.</param>
/// <param name="PermisosProvisionales">
/// GateProvisional mientras la fuente es PeachEBills y el área no cargó las llaves nuevas en allowAction: llaves que también lo habilitan
/// (lista vacía = visible para cualquier usuario con la empresa). null = sin régimen provisional (valen solo las reales).
/// </param>
/// <param name="RutasAdicionales">Otras rutas del mismo módulo que no cuelgan de <paramref name="Ruta"/> (las que sí cuelgan ya quedan cubiertas).</param>
public sealed record AppWeb(
    string Id,
    string Nombre,
    string Descripcion,
    string Icono,
    string Ruta,
    string Categoria,
    IReadOnlyList<string> PermisosQueLaHabilitan,
    IReadOnlyList<string>? PermisosProvisionales = null,
    IReadOnlyList<string>? RutasAdicionales = null)
{
    /// <summary>
    /// Visible (y accesible: la guardia de rutas y los endpoints usan esto mismo) si el usuario tiene al menos una de las llaves.
    /// Con <see cref="AppCatalogo.ModoProvisional"/> rige <see cref="PermisosProvisionales"/> si el módulo lo tiene.
    /// </summary>
    public bool VisiblePara(ContextoDeUsuario ctx) => VisiblePara(ctx, AppCatalogo.ModoProvisional);

    /// <summary>Igual, con el modo explícito (para pruebas: el modo global es estado compartido).</summary>
    public bool VisiblePara(ContextoDeUsuario ctx, bool modoProvisional)
    {
        if (modoProvisional && PermisosProvisionales is not null)
        {
            return PermisosProvisionales.Count == 0
                || PermisosProvisionales.Any(ctx.Puede)
                || PermisosQueLaHabilitan.Any(ctx.Puede);
        }
        return PermisosQueLaHabilitan.Any(ctx.Puede);
    }

    public IEnumerable<string> Rutas => RutasAdicionales is null ? new[] { Ruta } : RutasAdicionales.Prepend(Ruta);
}

/// <summary>
/// Catálogo de módulos web. Por ahora en código; puede pasar a una tabla
/// (<c>AppRegistry</c> en <c>PsaWebPlataforma</c>) en F-Shell-4.
/// </summary>
public static class AppCatalogo
{
    /// <summary>
    /// true mientras la fuente de accesos sea PeachEBills (lo fija <see cref="AccesosOptions.AplicarModo"/> al arrancar): los módulos con
    /// <see cref="AppWeb.PermisosProvisionales"/> siguen abiertos como antes. Con la fuente Web queda en false y no hay GateProvisional.
    /// </summary>
    public static bool ModoProvisional { get; set; } = true;

    private static readonly string[] Abierto = Array.Empty<string>();

    public static readonly IReadOnlyList<AppWeb> Todas = new List<AppWeb>
    {
        new("cierre-de-caja", "Cierre de Caja",
            "Cobros contra ventas registradas en Sage 50.",
            "💵", "/cierre-de-caja", Categorias.Caja,
            new[] { Permisos.VerCierreCaja },
            PermisosProvisionales: Abierto), // el piloto nunca tuvo llave

        // GateProvisional (fuente PeachEBills): abierto mientras el área no asigne quRptPwc (docs/sql/permisos-reportes-access.sql).
        new("reporte-pwc", "PWC — Cuentas por cobrar",
            "Facturas de venta con saldo, retenciones y monto a cobrar (ex reporte de Access).",
            "📋", "/cartera/pwc", Categorias.Cartera,
            new[] { Permisos.VerReportePwc },
            PermisosProvisionales: Abierto),

        new("reporte-comisiones", "Comisiones por recibos",
            "Facturas cobradas por recibos de cobro, agrupadas por cliente (ex reporte de Access).",
            "🧮", "/cartera/comisiones", Categorias.Cartera,
            new[] { Permisos.VerReporteComisiones },
            PermisosProvisionales: Abierto),

        // Ola 2. GateProvisional (fuente PeachEBills): también la habilitan las llaves de retenciones de compra (docs/sql/permisos-compras-ventas.sql).
        new("compras", "Compras",
            "Registra en Sage 50 las compras (facturas, notas de venta y liquidaciones) por el Sage Bridge.",
            "🛒", "/compras", Categorias.Compras,
            new[] { Permisos.VerCompras, Permisos.RegistrarCompras },
            PermisosProvisionales: new[] { Permisos.VerRetenciones, Permisos.HacerRetencion }),

        new("compras-recibidos", "Facturas recibidas del SRI",
            "Registra en Sage las facturas recibidas desde su XML (bandeja de documentos recibidos).",
            "📨", "/compras/recibidos", Categorias.Compras,
            new[] { Permisos.VerCompras, Permisos.RegistrarCompras },
            PermisosProvisionales: new[] { Permisos.VerRetenciones, Permisos.HacerRetencion }),

        new("compras-importaciones", "Liquidación de importaciones",
            "Prorratea los gastos de una importación a sus ítems y registra la OC y la compra en Sage 50 por el Sage Bridge.",
            "🚢", "/compras/importaciones", Categorias.Compras,
            new[] { Permisos.VerLiquidacionImportaciones, Permisos.RegistrarLiquidacionImportaciones },
            PermisosProvisionales: new[] { Permisos.VerCompras, Permisos.RegistrarCompras, Permisos.VerRetenciones, Permisos.HacerRetencion },
            RutasAdicionales: new[] { "/exportar/liquidacion-importacion" }),

        new("cheques", "Cheques y comprobantes de egreso",
            "Imprime el cheque (matricial) y el comprobante de egreso de los pagos de Sage 50.",
            "🖨️", "/bancos/cheques", Categorias.Bancos,
            new[] { Permisos.VerReporteCheques },
            PermisosProvisionales: Abierto),

        // GateProvisional (fuente PeachEBills): la fila quats existe en allowAction pero sin roles (verificado 2026-10-05).
        new("ats", "ATS",
            "Genera el XML del Anexo Transaccional Simplificado (ATS) del SRI a partir de Sage 50.",
            "📑", "/ats", Categorias.Impuestos,
            new[] { Permisos.VerAts },
            PermisosProvisionales: Abierto),

        new("conciliacion-sri", "Conciliación con SRI - Docs Recibidos",
            "Concilia contra Sage 50 las facturas, notas de crédito y retenciones recibidas del SRI.",
            "🔎", "/conciliacion-sri", Categorias.Impuestos,
            new[] { Permisos.VerConciliacionSri },
            PermisosProvisionales: Abierto),

        new("fe-facturas", "Facturas de venta emitidas",
            "Genera y emite en Datil las facturas de venta de Sage 50.",
            "🧾", "/fe/facturas", Categorias.ComprobantesElectronicos,
            new[] { Permisos.VerFacturas, Permisos.HacerFactura, Permisos.HacerFacturasLote }),

        new("retenciones", "Retenciones de compra emitidas",
            "Genera y emite en Datil las retenciones de compra pendientes.",
            "📄", "/fe/retenciones", Categorias.ComprobantesElectronicos,
            new[] { Permisos.VerRetenciones, Permisos.HacerRetencion, Permisos.HacerRetencionesLote },
            RutasAdicionales: new[] { "/retenciones" }),

        new("fe-liquidaciones", "Liquidaciones de compra emitidas",
            "Genera y emite en Datil las liquidaciones de compra.",
            "📥", "/fe/liquidaciones", Categorias.ComprobantesElectronicos,
            new[] { Permisos.VerLiquidaciones, Permisos.HacerLiquidacion, Permisos.HacerLiquidacionesLote }),

        new("fe-notas-credito", "Notas de crédito emitidas",
            "Genera y emite en Datil las notas de crédito de venta.",
            "↩️", "/fe/notas-credito", Categorias.ComprobantesElectronicos,
            new[] { Permisos.VerNotasCredito, Permisos.HacerNotaCredito, Permisos.HacerNotasCreditoLote }),

        // Portal de ventas (docs/PLAN-PORTAL-VENTAS.md). GateProvisional (fuente PeachEBills): también las llaves de facturación de venta.
        new("ventas-inventario", "Inventario y precios",
            "Existencias en tiempo real y precios por lista de Sage 50, con ficha y cupo del cliente (solo lectura).",
            "🏷️", "/ventas/inventario", Categorias.Ventas,
            new[] { Permisos.VerInventarioVentas, Permisos.VerPrefacturas, Permisos.EmitirPrefactura, Permisos.CerrarPrefactura },
            PermisosProvisionales: new[] { Permisos.VerFacturas, Permisos.HacerFactura }),

        new("ventas-prefacturas", "Prefacturas",
            "Cotizaciones con PDF y correo a Contabilidad para facturar en Sage (no escribe en Sage).",
            "🧾", "/ventas/prefacturas", Categorias.Ventas,
            new[] { Permisos.VerPrefacturas, Permisos.EmitirPrefactura, Permisos.CerrarPrefactura },
            PermisosProvisionales: new[] { Permisos.VerFacturas, Permisos.HacerFactura }),

        new("kardex", "Kardex",
            "Kardex de inventarios de Sage 50 (solo lectura).",
            "📦", "/kardex", Categorias.Inventario,
            new[] { Permisos.VerKardex },
            PermisosProvisionales: Abierto),
    };

    /// <summary>
    /// Módulos que escriben en Sage por el Bridge (Ola 2). Solo se ofrecen si <see cref="EscrituraOptions.Habilitada"/> (producción los mantiene
    /// apagados hasta el deploy de escritura).
    /// </summary>
    public static readonly IReadOnlySet<string> IdsDeEscritura = new HashSet<string> { "compras", "compras-recibidos", "compras-importaciones" };

    public static IEnumerable<AppWeb> Habilitadas(ContextoDeUsuario ctx, bool escrituraHabilitada = true) =>
        Todas.Where(a => (escrituraHabilitada || !IdsDeEscritura.Contains(a.Id)) && a.VisiblePara(ctx));

    public static AppWeb? PorId(string id) => Todas.FirstOrDefault(a => a.Id == id);

    /// <summary>
    /// Módulo al que pertenece una ruta (la ruta del módulo o cualquier subruta; gana la más larga: <c>/compras/recibidos/…</c> es de
    /// «Facturas recibidas», no de «Compras»). null = no es de ningún módulo (inicio, cuenta, admin, shell).
    /// </summary>
    public static AppWeb? ModuloDeRuta(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return null;
        var path = ruta;
        var corte = path.IndexOfAny(new[] { '?', '#' });
        if (corte >= 0) path = path[..corte];
        if (!path.StartsWith('/')) path = "/" + path;
        path = path.TrimEnd('/');

        AppWeb? mejor = null;
        var largo = -1;
        foreach (var app in Todas)
        {
            foreach (var r in app.Rutas)
            {
                var coincide = path.Equals(r, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase);
                if (coincide && r.Length > largo)
                {
                    mejor = app;
                    largo = r.Length;
                }
            }
        }
        return mejor;
    }
}
