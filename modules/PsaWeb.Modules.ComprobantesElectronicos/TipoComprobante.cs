using PsaWeb.Seguridad;

namespace PsaWeb.Modules.ComprobantesElectronicos;

/// <summary>
/// Los cuatro comprobantes electrónicos que comparten módulo, interfaz y funciones.
/// El valor numérico coincide con el orden del menú.
/// </summary>
public enum TipoComprobante
{
    Factura,
    Retencion,
    NotaCredito,
    Liquidacion,
}

/// <summary>Todo lo que distingue a un tipo de comprobante de otro (el resto es común).</summary>
public sealed record InfoTipo(
    TipoComprobante Tipo,
    // Código SRI del comprobante (01 factura, 07 retención, 04 nota de crédito, 03 liquidación).
    string CodDoc,
    string Titulo,
    string Subtitulo,
    // "factura", "retención", … — para mensajes.
    string Singular,
    // Etiqueta de la contraparte: Cliente o Proveedor.
    string PersonaEtiqueta,
    string PermisoVer,
    string PermisoHacer,
    string PermisoLote,
    string PermisoAnular,
    string Ruta)
{
    /// <summary>Las retenciones no llevan IVA ni total: se muestra el total retenido.</summary>
    public bool EsRetencion => Tipo == TipoComprobante.Retencion;
}

public static class Tipos
{
    public static IReadOnlyList<InfoTipo> Todos { get; } = new[]
    {
        new InfoTipo(TipoComprobante.Factura, "01", "Facturas de venta emitidas",
            "Genera y emite en Datil las facturas de venta registradas en Sage 50.",
            "factura", "Cliente",
            Permisos.VerFacturas, Permisos.HacerFactura, Permisos.HacerFacturasLote,
            Permisos.AutorizarAnulacionFactura, "/fe/facturas"),

        new InfoTipo(TipoComprobante.Retencion, "07", "Retenciones de compra emitidas",
            "Genera y emite en Datil las retenciones de las facturas de compra registradas en Sage 50.",
            "retención", "Proveedor",
            Permisos.VerRetenciones, Permisos.HacerRetencion, Permisos.HacerRetencionesLote,
            Permisos.AutorizarAnulacionRetencion, "/fe/retenciones"),

        new InfoTipo(TipoComprobante.NotaCredito, "04", "Notas de crédito emitidas",
            "Genera y emite en Datil las notas de crédito de venta registradas en Sage 50.",
            "nota de crédito", "Cliente",
            Permisos.VerNotasCredito, Permisos.HacerNotaCredito, Permisos.HacerNotasCreditoLote,
            Permisos.AutorizarAnulacionNotaCredito, "/fe/notas-credito"),

        new InfoTipo(TipoComprobante.Liquidacion, "03", "Liquidaciones de compra emitidas",
            "Genera y emite en Datil las liquidaciones de compra registradas en Sage 50.",
            "liquidación de compra", "Proveedor",
            Permisos.VerLiquidaciones, Permisos.HacerLiquidacion, Permisos.HacerLiquidacionesLote,
            Permisos.AutorizarAnulacionLiquidacion, "/fe/liquidaciones"),
    };

    public static InfoTipo De(TipoComprobante tipo) => Todos.Single(t => t.Tipo == tipo);

    public static InfoTipo PorCodDoc(string codDoc) =>
        Todos.Single(t => t.CodDoc == codDoc.Trim());
}
