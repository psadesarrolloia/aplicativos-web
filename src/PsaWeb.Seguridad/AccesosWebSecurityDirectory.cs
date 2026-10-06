using Microsoft.EntityFrameworkCore;
using PsaWeb.Identidad;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Seguridad;

/// <summary>Una empresa de PeachEBills (<c>Transmitter</c>) para el panel de accesos.</summary>
public sealed record EmpresaCatalogo(string Ruc, string Nombre, bool Activa);

/// <summary>Las empresas que existen (para asignarlas desde el panel). Solo lectura de PeachEBills.</summary>
public interface ICatalogoEmpresas
{
    Task<IReadOnlyList<EmpresaCatalogo>> TodasAsync(CancellationToken cancellationToken = default);
}

public sealed class CatalogoEmpresasPeachEbills(IDbContextFactory<PeachEbillsContext> peach) : ICatalogoEmpresas
{
    public async Task<IReadOnlyList<EmpresaCatalogo>> TodasAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await peach.CreateDbContextAsync(cancellationToken);
        var empresas = await db.Transmitter.AsNoTracking()
            .Select(t => new { t.Ruc, t.Name, t.NameAlias })
            .ToListAsync(cancellationToken);
        var activas = await db.TransmitterStatus.AsNoTracking()
            .Where(s => s.IsActive)
            .Select(s => s.TransmitterRuc)
            .ToListAsync(cancellationToken);
        var setActivas = activas.ToHashSet();
        return empresas
            .Select(e => new EmpresaCatalogo(e.Ruc, Nombre(e.NameAlias, e.Name, e.Ruc), setActivas.Contains(e.Ruc)))
            .OrderBy(e => e.Nombre, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Nombre comercial o razón social, sin los saltos de línea que traen algunos registros (hallazgo de la auditoría).</summary>
    internal static string Nombre(string? alias, string? razon, string ruc)
    {
        var n = string.IsNullOrWhiteSpace(alias) ? razon : alias;
        n = (n ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return n.Length == 0 ? ruc : n;
    }
}

/// <summary>
/// <see cref="ISecurityDirectory"/> sobre la tabla propia de accesos web (<c>AccesosEmpresa</c>/<c>AccesosLlave</c> en PsaWebPlataforma),
/// administrada desde /admin/accesos (PLAN-ACCESOS-WEB). PeachEBills solo aporta los nombres de las empresas y, como respaldo, los
/// correos de supervisores. Un usuario web desactivado no tiene empresas ni permisos.
/// </summary>
public sealed class AccesosWebSecurityDirectory : ISecurityDirectory
{
    /// <summary>Llaves que equivalen al rol «Supervisor» del .exe (autorizan anulaciones): reciben las solicitudes de anulación.</summary>
    private static readonly string[] LlavesSupervisor =
    {
        Permisos.AutorizarAnulacionFactura, Permisos.AutorizarAnulacionNotaCredito,
        Permisos.AutorizarAnulacionLiquidacion, Permisos.AutorizarAnulacionRetencion,
    };

    private readonly DbContextOptions<PlataformaDbContext> _plataforma;
    private readonly ICatalogoEmpresas _empresas;
    private readonly ISecurityDirectory _peachEbills;

    public AccesosWebSecurityDirectory(
        DbContextOptions<PlataformaDbContext> plataforma, ICatalogoEmpresas empresas, PeachEbillsSecurityDirectory peachEbills)
    {
        _plataforma = plataforma;
        _empresas = empresas;
        _peachEbills = peachEbills;
    }

    private PlataformaDbContext Db() => new(_plataforma);

    public async Task<IReadOnlyList<EmpresaDelUsuario>> EmpresasDelUsuarioAsync(string usuario, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return Array.Empty<EmpresaDelUsuario>();

        await using var db = Db();
        var rucs = await (
            from u in db.Users.AsNoTracking()
            where u.UserName == usuario && u.Activo
            join a in db.AccesosEmpresa.AsNoTracking() on u.Id equals a.UsuarioId
            where a.Activo
            select a.Ruc)
            .ToListAsync(cancellationToken);
        if (rucs.Count == 0) return Array.Empty<EmpresaDelUsuario>();

        var nombres = (await _empresas.TodasAsync(cancellationToken)).ToDictionary(e => e.Ruc, e => e.Nombre);
        return rucs
            .Select(r => (Ruc: r, Nombre: nombres.GetValueOrDefault(r, r)))
            .OrderBy(e => e.Nombre, StringComparer.OrdinalIgnoreCase)
            .Select((e, i) => new EmpresaDelUsuario(e.Ruc, e.Nombre, i + 1))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<string, int>> ContarEmpresasAsync(IEnumerable<string> usuarios, CancellationToken cancellationToken = default)
    {
        var lista = usuarios.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (lista.Length == 0) return new Dictionary<string, int>();

        await using var db = Db();
        var filas = await (
            from u in db.Users.AsNoTracking()
            where EF.Constant(lista).Contains(u.UserName!)
            join a in db.AccesosEmpresa.AsNoTracking() on u.Id equals a.UsuarioId
            where a.Activo
            group a by u.UserName into g
            select new { Usuario = g.Key!, Cantidad = g.Count() })
            .ToListAsync(cancellationToken);
        return filas.ToDictionary(f => f.Usuario, f => f.Cantidad, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlySet<string>> PermisosAsync(string usuario, string ruc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(ruc)) return new HashSet<string>();

        await using var db = Db();
        var llaves = await (
            from u in db.Users.AsNoTracking()
            where u.UserName == usuario && u.Activo
            join a in db.AccesosEmpresa.AsNoTracking() on u.Id equals a.UsuarioId
            where a.Ruc == ruc && a.Activo
            join l in db.AccesosLlave.AsNoTracking() on new { a.UsuarioId, a.Ruc } equals new { l.UsuarioId, l.Ruc }
            select l.Llave)
            .Distinct()
            .ToListAsync(cancellationToken);
        return llaves.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<VinculoSage> VinculoSageAsync(string usuario, string ruc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(ruc)) return new VinculoSage(true, null);
        await using var db = Db();
        var sage = await (
            from u in db.Users.AsNoTracking()
            where u.UserName == usuario && u.Activo
            join a in db.AccesosEmpresa.AsNoTracking() on u.Id equals a.UsuarioId
            where a.Ruc == ruc && a.Activo
            select a.UsuarioSage)
            .FirstOrDefaultAsync(cancellationToken);
        return new VinculoSage(true, string.IsNullOrWhiteSpace(sage) ? null : sage.Trim());
    }

    public async Task<string?> EmailUsuarioAsync(string usuario, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return null;
        await using var db = Db();
        var email = await db.Users.AsNoTracking()
            .Where(u => u.UserName == usuario)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(email) ? await _peachEbills.EmailUsuarioAsync(usuario, cancellationToken) : email.Trim();
    }

    public async Task<IReadOnlyList<string>> EmailsPorRolAsync(string ruc, string rol = "Supervisor", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ruc)) return Array.Empty<string>();
        if (!string.Equals(rol, "Supervisor", StringComparison.OrdinalIgnoreCase))
        {
            return await _peachEbills.EmailsPorRolAsync(ruc, rol, cancellationToken);
        }

        await using var db = Db();
        var emails = await (
            from u in db.Users.AsNoTracking()
            where u.Activo && u.Email != null && u.Email != ""
            join a in db.AccesosEmpresa.AsNoTracking() on u.Id equals a.UsuarioId
            where a.Ruc == ruc && a.Activo
            join l in db.AccesosLlave.AsNoTracking() on new { a.UsuarioId, a.Ruc } equals new { l.UsuarioId, l.Ruc }
            where EF.Constant(LlavesSupervisor).Contains(l.Llave)
            select u.Email!)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Mientras nadie tenga llaves de anulación en la web, siguen recibiendo los supervisores del .exe.
        return emails.Count > 0
            ? emails.Select(e => e.Trim()).ToList()
            : await _peachEbills.EmailsPorRolAsync(ruc, rol, cancellationToken);
    }
}
