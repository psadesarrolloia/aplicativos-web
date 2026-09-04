using Microsoft.EntityFrameworkCore;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Seguridad;

/// <summary>
/// Implementación de <see cref="ISecurityDirectory"/> sobre las tablas heredadas
/// de PeachEBills (<c>user</c>, <c>UserTransmitter</c>, <c>udrUserRolesTr</c>,
/// <c>adrAllowRol</c>). Solo lectura.
/// </summary>
public sealed class PeachEbillsSecurityDirectory : ISecurityDirectory
{
    private readonly IDbContextFactory<PeachEbillsContext> _contextFactory;

    public PeachEbillsSecurityDirectory(IDbContextFactory<PeachEbillsContext> contextFactory)
        => _contextFactory = contextFactory;

    /// <summary>Quita el dominio de un login Windows (<c>PAREDESSOLUCION\jperez</c> → <c>jperez</c>).</summary>
    public static string NormalizarUsuario(string? usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return string.Empty;
        var i = usuario.IndexOf('\\');
        var sam = i >= 0 ? usuario[(i + 1)..] : usuario;
        i = sam.IndexOf('@'); // UPN jperez@paredes.local
        if (i > 0) sam = sam[..i];
        return sam.Trim();
    }

    public async Task<IReadOnlyList<EmpresaDelUsuario>> EmpresasDelUsuarioAsync(
        string usuario, CancellationToken cancellationToken = default)
    {
        var user = NormalizarUsuario(usuario);
        if (user.Length == 0) return Array.Empty<EmpresaDelUsuario>();

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var accesos = await db.SecUserTransmitters.AsNoTracking()
            .Where(ut => ut.User == user)
            .Select(ut => new { ut.Ruc, ut.Order })
            .ToListAsync(cancellationToken);

        if (accesos.Count == 0) return Array.Empty<EmpresaDelUsuario>();

        var rucs = accesos.Select(a => a.Ruc).Distinct().ToArray();
        var nombres = await db.Transmitter.AsNoTracking()
            .Where(t => EF.Constant(rucs).Contains(t.Ruc))
            .Select(t => new { t.Ruc, Nombre = t.NameAlias ?? t.Name })
            .ToListAsync(cancellationToken);
        var mapaNombre = nombres.ToDictionary(x => x.Ruc, x => x.Nombre);

        return accesos
            .GroupBy(a => a.Ruc)
            .Select(g => new EmpresaDelUsuario(
                g.Key,
                mapaNombre.GetValueOrDefault(g.Key, g.Key),
                g.Min(x => x.Order)))
            .OrderBy(e => e.Orden)
            .ThenBy(e => e.Nombre, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlySet<string>> PermisosAsync(
        string usuario, string ruc, CancellationToken cancellationToken = default)
    {
        var user = NormalizarUsuario(usuario);
        if (user.Length == 0 || string.IsNullOrWhiteSpace(ruc))
            return new HashSet<string>();

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // roles del usuario en esa empresa -> permisos activos de esos roles
        var codigos = await (
            from ur in db.SecUserRoles.AsNoTracking()
            where ur.User == user && ur.Ruc == ruc
            join rp in db.SecRolePermissions.AsNoTracking() on ur.Rol equals rp.Rol
            where rp.Active
            select rp.AllowCode)
            .Distinct()
            .ToListAsync(cancellationToken);

        return codigos.ToHashSet(StringComparer.Ordinal);
    }
}
