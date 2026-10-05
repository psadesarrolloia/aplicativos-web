namespace PsaWeb.Seguridad;

/// <summary>
/// Quién hace qué en el portal de ventas (docs/PLAN-PORTAL-VENTAS.md):
/// <list type="bullet">
///   <item><b>Inventario y precios</b>: <c>quSalesStk</c> o cualquiera de las demás.</item>
///   <item><b>Ver prefacturas</b>: <c>quSalesQte</c>, o emitir, o cerrar. Un usuario sin <c>auSalesQte</c> ve <b>solo las que emitió él</b>.</item>
///   <item><b>Emitir</b> (<c>mkSalesQte</c>): arma la prefactura, descarga el PDF y reenvía el correo de las suyas.</item>
///   <item><b>Cerrar</b> (<c>auSalesQte</c>, Contabilidad): ve todas las de la empresa, anota la factura de Sage y anula.</item>
/// </list>
/// </summary>
public static class ReglasVentas
{
    /// <summary>
    /// GateProvisional: mientras el área no cargue las 4 llaves nuevas (<c>docs/sql/permisos-ventas.sql</c>), también habilitan las llaves de
    /// facturación electrónica de venta, que es de donde el script copia los roles (<c>qusaleinv</c> ver, <c>mksaleinv</c> emitir y cerrar).
    /// Poner en <c>false</c> cuando el script esté aplicado.
    /// </summary>
    public static bool PermisosProvisionales { get; set; } = true;

    public static bool PuedeCerrar(IReadOnlySet<string> permisos) =>
        permisos.Contains(Permisos.CerrarPrefactura) || (PermisosProvisionales && permisos.Contains(Permisos.HacerFactura));

    public static bool PuedeEmitir(IReadOnlySet<string> permisos) =>
        permisos.Contains(Permisos.EmitirPrefactura) || (PermisosProvisionales && permisos.Contains(Permisos.HacerFactura));

    public static bool PuedeVerPrefacturas(IReadOnlySet<string> permisos) =>
        permisos.Contains(Permisos.VerPrefacturas) || PuedeEmitir(permisos) || PuedeCerrar(permisos)
        || (PermisosProvisionales && permisos.Contains(Permisos.VerFacturas));

    public static bool PuedeVerInventario(IReadOnlySet<string> permisos) =>
        permisos.Contains(Permisos.VerInventarioVentas) || PuedeVerPrefacturas(permisos);

    /// <summary>Ver las prefacturas de todos los vendedores de la empresa (no solo las propias).</summary>
    public static bool PuedeVerTodas(IReadOnlySet<string> permisos) => PuedeCerrar(permisos);
}
