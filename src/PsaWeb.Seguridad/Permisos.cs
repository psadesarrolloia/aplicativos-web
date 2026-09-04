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

    // Retenciones
    public const string VerRetenciones = "qupurchtwh";
    public const string HacerRetencion = "mkpurchtwh";
    public const string HacerRetencionesLote = "mkTwhBatch";
    public const string AutorizarAnulacionRetencion = "auCanceTwh";

    // ATS
    public const string VerAts = "quats";

    // Configuración
    public const string ConfigurarDatil = "setDatilP";
    public const string ConfigurarOdbc = "setODBC";
}
