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
}
