namespace PsaWeb.Seguridad;

/// <summary>Una llave que el panel de accesos puede otorgar, con el módulo al que pertenece y su nivel legible.</summary>
public sealed record LlaveWeb(string Codigo, string AppId, string Nivel, bool EscribeEnSage = false);

/// <summary>
/// Catálogo de llaves del panel /admin/accesos (agrupadas por módulo de <see cref="AppCatalogo"/>) y plantillas de perfil
/// (PLAN-ACCESOS-WEB §5).
/// </summary>
public static class LlavesWeb
{
    public static readonly IReadOnlyList<LlaveWeb> Todas = new[]
    {
        new LlaveWeb(Permisos.VerCierreCaja, "cierre-de-caja", "Ver"),
        new LlaveWeb(Permisos.VerReportePwc, "reporte-pwc", "Ver"),
        new LlaveWeb(Permisos.VerReporteComisiones, "reporte-comisiones", "Ver"),
        new LlaveWeb(Permisos.VerCompras, "compras", "Ver", EscribeEnSage: true),
        new LlaveWeb(Permisos.RegistrarCompras, "compras", "Registrar en Sage", EscribeEnSage: true),
        new LlaveWeb(Permisos.VerLiquidacionImportaciones, "compras-importaciones", "Ver", EscribeEnSage: true),
        new LlaveWeb(Permisos.RegistrarLiquidacionImportaciones, "compras-importaciones", "Registrar en Sage", EscribeEnSage: true),
        new LlaveWeb(Permisos.VerReporteCheques, "cheques", "Ver e imprimir"),
        new LlaveWeb(Permisos.VerAts, "ats", "Ver y descargar"),
        new LlaveWeb(Permisos.VerConciliacionSri, "conciliacion-sri", "Ver y subir (extensión)"),
        new LlaveWeb(Permisos.VerFacturas, "fe-facturas", "Ver"),
        new LlaveWeb(Permisos.HacerFactura, "fe-facturas", "Emitir"),
        new LlaveWeb(Permisos.HacerFacturasLote, "fe-facturas", "Por lote"),
        new LlaveWeb(Permisos.AutorizarAnulacionFactura, "fe-facturas", "Autorizar anulación"),
        new LlaveWeb(Permisos.VerRetenciones, "retenciones", "Ver"),
        new LlaveWeb(Permisos.HacerRetencion, "retenciones", "Emitir"),
        new LlaveWeb(Permisos.HacerRetencionesLote, "retenciones", "Por lote / todas las empresas"),
        new LlaveWeb(Permisos.AutorizarAnulacionRetencion, "retenciones", "Autorizar anulación"),
        new LlaveWeb(Permisos.VerLiquidaciones, "fe-liquidaciones", "Ver"),
        new LlaveWeb(Permisos.HacerLiquidacion, "fe-liquidaciones", "Emitir"),
        new LlaveWeb(Permisos.HacerLiquidacionesLote, "fe-liquidaciones", "Por lote"),
        new LlaveWeb(Permisos.AutorizarAnulacionLiquidacion, "fe-liquidaciones", "Autorizar anulación"),
        new LlaveWeb(Permisos.VerNotasCredito, "fe-notas-credito", "Ver"),
        new LlaveWeb(Permisos.HacerNotaCredito, "fe-notas-credito", "Emitir"),
        new LlaveWeb(Permisos.HacerNotasCreditoLote, "fe-notas-credito", "Por lote"),
        new LlaveWeb(Permisos.AutorizarAnulacionNotaCredito, "fe-notas-credito", "Autorizar anulación"),
        new LlaveWeb(Permisos.VerInventarioVentas, "ventas-inventario", "Ver inventario y precios"),
        new LlaveWeb(Permisos.VerPrefacturas, "ventas-prefacturas", "Ver las propias"),
        new LlaveWeb(Permisos.EmitirPrefactura, "ventas-prefacturas", "Emitir"),
        new LlaveWeb(Permisos.CerrarPrefactura, "ventas-prefacturas", "Contabilidad (todas, cerrar, anular)"),
        new LlaveWeb(Permisos.VerKardex, "kardex", "Ver"),
    };

    private static readonly IReadOnlySet<string> Codigos = Todas.Select(l => l.Codigo).ToHashSet(StringComparer.Ordinal);

    public static bool Existe(string? codigo) => codigo is not null && Codigos.Contains(codigo);

    /// <summary>Las llaves de un módulo, en el orden del catálogo.</summary>
    public static IReadOnlyList<LlaveWeb> DeModulo(string appId) => Todas.Where(l => l.AppId == appId).ToList();

    /// <summary>Plantillas de perfil para «Aplicar plantilla» y la siembra (PLAN-ACCESOS-WEB §5).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Plantillas = new Dictionary<string, IReadOnlyList<string>>
    {
        // Super Admin y Admin: todo. Las de escritura en Sage no hacen nada mientras Escritura:Habilitada=false.
        ["Super Admin / Admin (todo)"] = Todas.Select(l => l.Codigo).ToList(),

        // Lo que hoy da el rol del .exe «Hacer Comprobantes Electrónicos» + Conciliación SRI.
        ["Digitador/a"] = new[]
        {
            Permisos.VerFacturas, Permisos.HacerFactura, Permisos.VerNotasCredito, Permisos.HacerNotaCredito,
            Permisos.VerLiquidaciones, Permisos.HacerLiquidacion, Permisos.VerRetenciones, Permisos.HacerRetencion,
            Permisos.VerReportePwc, Permisos.VerReporteComisiones, Permisos.VerReporteCheques, Permisos.VerConciliacionSri,
        },

        // Nivel entre Digitador/a y Admin (decisión del usuario 2026-10-06): lo del digitador + autorizar anulaciones de los 4 comprobantes.
        ["Supervisor"] = new[]
        {
            Permisos.VerFacturas, Permisos.HacerFactura, Permisos.VerNotasCredito, Permisos.HacerNotaCredito,
            Permisos.VerLiquidaciones, Permisos.HacerLiquidacion, Permisos.VerRetenciones, Permisos.HacerRetencion,
            Permisos.VerReportePwc, Permisos.VerReporteComisiones, Permisos.VerReporteCheques, Permisos.VerConciliacionSri,
            Permisos.AutorizarAnulacionFactura, Permisos.AutorizarAnulacionNotaCredito,
            Permisos.AutorizarAnulacionLiquidacion, Permisos.AutorizarAnulacionRetencion,
        },

        ["Vendedor"] = new[] { Permisos.VerInventarioVentas, Permisos.VerPrefacturas, Permisos.EmitirPrefactura },

        ["Contabilidad de Ventas"] = new[] { Permisos.VerInventarioVentas, Permisos.CerrarPrefactura },

        ["Ninguna (vaciar)"] = Array.Empty<string>(),
    };
}

/// <summary>
/// Nivel de trabajo de una cuenta, deducido de sus llaves (para mostrarlo junto al perfil del panel). Se mira el conjunto de llaves de todas
/// sus empresas activas: el nivel más alto que alcance en alguna.
/// </summary>
public static class NivelesWeb
{
    public const string SinModulos = "Sin módulos";

    private static readonly string[] Anulaciones =
    {
        Permisos.AutorizarAnulacionFactura, Permisos.AutorizarAnulacionNotaCredito,
        Permisos.AutorizarAnulacionLiquidacion, Permisos.AutorizarAnulacionRetencion,
    };

    private static readonly string[] Emision =
    {
        Permisos.HacerFactura, Permisos.HacerNotaCredito, Permisos.HacerLiquidacion, Permisos.HacerRetencion,
        Permisos.HacerFacturasLote, Permisos.HacerNotasCreditoLote, Permisos.HacerLiquidacionesLote, Permisos.HacerRetencionesLote,
        Permisos.RegistrarCompras, Permisos.RegistrarLiquidacionImportaciones,
    };

    private static readonly string[] Ventas = { Permisos.VerInventarioVentas, Permisos.VerPrefacturas, Permisos.EmitirPrefactura };

    /// <param name="perfilEfectivo">Perfil del panel (Super Admin y Admin se muestran como tales).</param>
    public static string Describir(string perfilEfectivo, IEnumerable<string> llaves)
    {
        if (perfilEfectivo == Identidad.Perfiles.SuperAdmin) return "Super Admin";
        if (perfilEfectivo == Identidad.Perfiles.Admin) return "Admin";
        var set = llaves.ToHashSet(StringComparer.Ordinal);
        if (set.Count == 0) return SinModulos;
        if (Anulaciones.Any(set.Contains)) return "Supervisor";
        if (Emision.Any(set.Contains)) return "Digitador";
        if (set.Contains(Permisos.CerrarPrefactura)) return "Contabilidad de Ventas";
        if (set.Contains(Permisos.EmitirPrefactura) || set.All(Ventas.Contains)) return "Vendedor";
        return "Consulta";
    }
}
