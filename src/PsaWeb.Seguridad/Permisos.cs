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

    // Compras (Ola 2) — códigos nuevos (docs/sql/permisos-compras-ventas.sql copia los roles de qupurchtwh/mkpurchtwh).
    public const string VerCompras = "qupurchinv";
    public const string RegistrarCompras = "mkpurchinv";

    // Liquidación de importaciones (Ola 2) — códigos nuevos (el .exe no la gateaba; docs/sql/permisos-compras-ventas.sql copia
    // los roles de qupurchtwh/mkpurchtwh).
    public const string VerLiquidacionImportaciones = "quimpliq";
    public const string RegistrarLiquidacionImportaciones = "mkimpliq";

    // Portal de ventas — códigos nuevos (docs/sql/permisos-ventas.sql copia los roles de qusaleinv/mksaleinv; allowCode = nvarchar(10)).
    public const string VerInventarioVentas = "quSalesStk";   // inventario y precios, solo lectura
    public const string VerPrefacturas = "quSalesQte";        // ver prefacturas (las propias; Contabilidad ve todas)
    public const string EmitirPrefactura = "mkSalesQte";      // emitir prefactura/cotización (PDF + correo a Contabilidad)
    public const string CerrarPrefactura = "auSalesQte";      // Contabilidad: ver todas, marcar facturada en Sage, anular

    // ATS
    public const string VerAts = "quats";

    // Inventarios / reportes del monolito — código nuevo (el .exe no lo gateaba).
    // Requiere 1 fila nueva en allowAction de PeachEBills asignada a los roles.
    public const string VerKardex = "quKardex";

    // Reportes migrados de Access (Cartera / Bancos) — códigos nuevos (el .mdb no los gateaba).
    // Requieren 3 filas nuevas en allowAction (docs/sql/permisos-reportes-access.sql).
    public const string VerReportePwc = "quRptPwc";
    public const string VerReporteComisiones = "quRptComis";
    public const string VerReporteCheques = "quRptChq";

    // Conciliación SRI — módulo sin equivalente de escritorio, código nuevo.
    // Requiere 1 fila nueva en allowAction de PeachEBills asignada a los roles.
    public const string VerConciliacionSri = "quconcsri";

    // Cierre de Caja — código nuevo (el piloto no tenía llave: era visible para cualquier empresa). Solo existe en la tabla de
    // accesos web (PLAN-ACCESOS-WEB); no hace falta cargarlo en allowAction.
    public const string VerCierreCaja = "quCierre";

    // Configuración
    public const string ConfigurarDatil = "setDatilP";
    public const string ConfigurarOdbc = "setODBC";
}
