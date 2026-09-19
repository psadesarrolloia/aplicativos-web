namespace PsaWeb.Seguridad;

/// <summary>
/// Códigos de <c>allowAction</c> de PeachEBills (mismos que usan los aplicativos
/// de escritorio). Para apps nuevas se agregan filas a esa tabla y una constante acá.
/// </summary>
public static class Permisos
{
    // Facturación electrónica
    public const string VerFacturas = "qusaleinv";
    public const string HacerFactura = "mksaleinv";
    public const string HacerFacturasLote = "mksinBatch";
    public const string VerNotasCredito = "qusalenc";
    public const string HacerNotaCredito = "mksalenc";

    // Liquidaciones de compra — códigos nuevos (el .exe no las gateaba).
    // Filas creadas por docs/sql/permisos-comprobantes-v2.sql.
    public const string VerLiquidaciones = "qupurchliq";
    public const string HacerLiquidacion = "mkpurchliq";

    // Comprobantes electrónicos v2: mismas 4 llaves para los 4 tipos
    // (ver · hacer · lote · autorizar anulación). Filas nuevas en el mismo script SQL.
    public const string HacerNotasCreditoLote = "mkncBatch";
    public const string HacerLiquidacionesLote = "mkliqBatch";
    public const string AutorizarAnulacionFactura = "auCanceInv";
    public const string AutorizarAnulacionNotaCredito = "auCanceNc";
    public const string AutorizarAnulacionLiquidacion = "auCanceLiq";

    // Retenciones
    public const string VerRetenciones = "qupurchtwh";
    public const string HacerRetencion = "mkpurchtwh";
    public const string HacerRetencionesLote = "mkTwhBatch";
    public const string AutorizarAnulacionRetencion = "auCanceTwh";

    // ATS
    public const string VerAts = "quats";

    // Inventarios / reportes del monolito — código nuevo (el .exe no lo gateaba).
    // Requiere 1 fila nueva en allowAction de PeachEBills asignada a los roles.
    public const string VerKardex = "quKardex";

    // Conciliación SRI — módulo sin equivalente de escritorio, código nuevo.
    // Requiere 1 fila nueva en allowAction de PeachEBills asignada a los roles.
    public const string VerConciliacionSri = "quconcsri";

    // Configuración
    public const string ConfigurarDatil = "setDatilP";
    public const string ConfigurarOdbc = "setODBC";
}
