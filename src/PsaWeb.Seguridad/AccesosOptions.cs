namespace PsaWeb.Seguridad;

/// <summary>De dónde salen las empresas y permisos de la web (PLAN-ACCESOS-WEB §4.1 y §7).</summary>
public enum FuenteAccesos
{
    /// <summary>Tablas del <c>.exe</c> en PeachEBills (comportamiento histórico). Los módulos sin llave cargada siguen con GateProvisional.</summary>
    PeachEBills,

    /// <summary>Tabla propia de accesos web en PsaWebPlataforma (panel /admin/accesos). Todo módulo cerrado salvo que se marque.</summary>
    Web,
}

/// <summary>Sección <c>Accesos</c>. <c>Accesos:Fuente</c> = <c>PeachEBills</c> (por defecto) | <c>Web</c>. Cambiarla exige reiniciar el pool.</summary>
public sealed class AccesosOptions
{
    public const string SectionName = "Accesos";

    public FuenteAccesos Fuente { get; set; } = FuenteAccesos.PeachEBills;

    /// <summary>
    /// Fija el modo de los catálogos estáticos según la fuente: con <see cref="FuenteAccesos.Web"/> no hay GateProvisional (cada módulo
    /// exige sus llaves); con PeachEBills sigue el provisional hasta que el área cargue las llaves faltantes en allowAction.
    /// </summary>
    public static void AplicarModo(FuenteAccesos fuente)
    {
        var provisional = fuente == FuenteAccesos.PeachEBills;
        AppCatalogo.ModoProvisional = provisional;
        ReglasVentas.PermisosProvisionales = provisional;
        ReglasCompras.PermisosProvisionales = provisional;
    }
}
