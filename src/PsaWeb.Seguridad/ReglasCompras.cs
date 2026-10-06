namespace PsaWeb.Seguridad;

/// <summary>
/// Quién ve y quién registra compras (módulo Compras y el botón «Registrar en Sage» de Conciliación, §4.2 y §9 del plan de la Ola 2).
/// </summary>
public static class ReglasCompras
{
    /// <summary>
    /// GateProvisional: mientras el área no cargue <c>qupurchinv</c>/<c>mkpurchinv</c> en <c>allowAction</c>
    /// (<c>docs/sql/permisos-compras-ventas.sql</c>), también habilitan las llaves de retenciones de compra, que son las que el
    /// script copia. Poner en <c>false</c> cuando el script esté aplicado.
    /// </summary>
    public static bool PermisosProvisionales { get; set; } = true;

    public static bool PuedeRegistrar(IReadOnlySet<string> permisos) =>
        permisos.Contains(Permisos.RegistrarCompras) || (PermisosProvisionales && permisos.Contains(Permisos.HacerRetencion));

    public static bool PuedeVer(IReadOnlySet<string> permisos) =>
        PuedeRegistrar(permisos) || permisos.Contains(Permisos.VerCompras)
        || (PermisosProvisionales && permisos.Contains(Permisos.VerRetenciones));

    /// <summary>
    /// Liquidación de importaciones: <c>mkimpliq</c> (guardar, crear OC y compra) / <c>quimpliq</c> (ver). GateProvisional: mientras
    /// no estén cargadas, las habilitan las mismas llaves que Compras.
    /// </summary>
    public static bool PuedeRegistrarLiquidaciones(IReadOnlySet<string> permisos) =>
        permisos.Contains(Permisos.RegistrarLiquidacionImportaciones) || (PermisosProvisionales && PuedeRegistrar(permisos));

    public static bool PuedeVerLiquidaciones(IReadOnlySet<string> permisos) =>
        PuedeRegistrarLiquidaciones(permisos) || permisos.Contains(Permisos.VerLiquidacionImportaciones)
        || (PermisosProvisionales && PuedeVer(permisos));
}

/// <summary>Mensajes comunes cuando alguien intenta escribir en Sage sin poder (revalidación en el servidor).</summary>
public static class MensajesEscrituraSage
{
    public const string SinPermiso = "No tienes permiso para registrar en Sage en esta empresa.";

    public const string SinUsuarioSage =
        "Tu cuenta no tiene usuario de Sage vinculado en esta empresa: pídelo a la administración (Configuración › Accesos).";

    /// <summary>Para la auditoría: «usuario» o «usuario (Sage: X)».</summary>
    public static string ConUsuarioSage(string usuario, string? usuarioSage) =>
        string.IsNullOrWhiteSpace(usuarioSage) ? usuario : $"{usuario} (Sage: {usuarioSage})";
}
