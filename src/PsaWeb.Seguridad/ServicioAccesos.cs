using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PsaWeb.Identidad;

namespace PsaWeb.Seguridad;

/// <summary>Qué puede hacer alguien en el panel de administración.</summary>
public enum RolPanel
{
    Ninguno,

    /// <summary>Activa, desactiva y reasigna empresas y módulos de usuarios existentes (no Super Admin, no a sí mismo). No crea usuarios.</summary>
    Admin,

    /// <summary>Todo: usuarios, perfiles, accesos, auditoría, configuración.</summary>
    SuperAdmin,
}

/// <summary>Reglas del panel (PLAN-ACCESOS-WEB §2 y §4.2), puras para poder probarlas sin base.</summary>
public static class ReglasPanel
{
    public static RolPanel RolDe(string perfilEfectivo) => perfilEfectivo switch
    {
        Perfiles.SuperAdmin => RolPanel.SuperAdmin,
        Perfiles.Admin => RolPanel.Admin,
        _ => RolPanel.Ninguno,
    };

    public static bool PuedeAdministrarUsuarios(RolPanel actor) => actor == RolPanel.SuperAdmin;

    public static bool PuedeVerAuditoria(RolPanel actor) => actor != RolPanel.Ninguno;

    /// <summary>Editar empresas, módulos, usuario de Sage y token de la extensión de otra cuenta.</summary>
    public static bool PuedeEditarAccesos(RolPanel actor, string actorId, string objetivoPerfilEfectivo, string objetivoId) => actor switch
    {
        RolPanel.SuperAdmin => true,
        RolPanel.Admin => objetivoPerfilEfectivo != Perfiles.SuperAdmin && !string.Equals(actorId, objetivoId, StringComparison.Ordinal),
        _ => false,
    };

    /// <summary>Nadie cambia su propio perfil (evita quedarse sin Super Admin por error); solo un Super Admin cambia perfiles.</summary>
    public static bool PuedeCambiarPerfil(RolPanel actor, string actorId, string objetivoId) =>
        actor == RolPanel.SuperAdmin && !string.Equals(actorId, objetivoId, StringComparison.Ordinal);
}

public sealed record UsuarioPanel(
    string Id, string UserName, string? Nombre, string? Email, bool EmailConfirmado, string Perfil, string PerfilEfectivo,
    bool Activo, bool DosFactores, string? Metodo, bool TieneClave, DateTime? UltimoAccesoUtc, int Empresas, string PeachUsername,
    string Nivel = NivelesWeb.SinModulos);

/// <summary>Estado de una empresa en la ficha de un usuario (asignada o no).</summary>
public sealed record AccesoEmpresaPanel(
    string Ruc, string Nombre, bool EmpresaActiva, bool Asignada, bool Activo, string? UsuarioSage, IReadOnlySet<string> Llaves,
    string? ModificadoPor, DateTime? ModificadoUtc);

public sealed class AccesoDenegadoException(string mensaje) : Exception(mensaje);

/// <summary>
/// Operaciones del panel /admin/accesos y /admin/usuarios sobre la tabla propia de accesos web. Cada cambio queda en la auditoría
/// (<see cref="AuditoriaAuth"/>) con quién, a quién y qué. Valida los permisos del actor en cada operación (no confía en la UI).
/// </summary>
public sealed class ServicioAccesos
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DbContextOptions<PlataformaDbContext> _plataforma;
    private readonly AuditoriaAuth _auditoria;
    private readonly ICatalogoEmpresas _empresas;
    private readonly PeachEbillsSecurityDirectory _peachEbills;
    private readonly IEnviadorCorreoPlataforma _correo;
    private readonly PlataformaOptions _opciones;

    // No se inyecta UserManager: en Blazor Server el scope dura todo el circuito y su DbContext lo comparten el menú, la guardia y
    // la página, que consultan a la vez ("A second operation was started on this context"). Cada operación abre su propio scope
    // (y así tampoco quedan entidades rastreadas viejas entre una acción y otra).
    public ServicioAccesos(
        IServiceScopeFactory scopes, DbContextOptions<PlataformaDbContext> plataforma, AuditoriaAuth auditoria,
        ICatalogoEmpresas empresas, PeachEbillsSecurityDirectory peachEbills, IEnviadorCorreoPlataforma correo,
        IOptions<PlataformaOptions> opciones)
    {
        _scopes = scopes;
        _plataforma = plataforma;
        _auditoria = auditoria;
        _empresas = empresas;
        _peachEbills = peachEbills;
        _correo = correo;
        _opciones = opciones.Value;
    }

    private PlataformaDbContext Db() => new(_plataforma);

    private AsyncServiceScope Ambito() => _scopes.CreateAsyncScope();

    private static UserManager<UsuarioApp> Usuarios(AsyncServiceScope ambito) => ambito.ServiceProvider.GetRequiredService<UserManager<UsuarioApp>>();

    private async Task<UsuarioApp?> LeerAsync(string usuarioId, CancellationToken ct = default)
    {
        await using var db = Db();
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
    }

    public bool CorreoDisponible => _correo.Disponible;

    // ---------------------------------------------------------------- actor

    public sealed record Actor(string Id, string UserName, RolPanel Rol);

    /// <summary>Quién está operando y con qué rol de panel (el perfil se lee de la base; <c>Plataforma:Admins</c> = Super Admin de respaldo).</summary>
    public async Task<Actor?> ActorAsync(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) return null;
        var normalizado = userName.Trim().ToUpperInvariant(); // mismo normalizador que Identity (UpperInvariantLookupNormalizer)
        await using var db = Db();
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.NormalizedUserName == normalizado);
        if (u is null || !u.Activo) return null;
        return new Actor(u.Id, u.UserName!, ReglasPanel.RolDe(UsuarioClaimsFactory.PerfilEfectivo(u, _opciones)));
    }

    private static void Exigir(bool condicion, string mensaje)
    {
        if (!condicion) throw new AccesoDenegadoException(mensaje);
    }

    private async Task<UsuarioApp> ObjetivoEditableAsync(Actor actor, string usuarioId)
    {
        var u = await LeerAsync(usuarioId) ?? throw new InvalidOperationException("No existe ese usuario.");
        Exigir(ReglasPanel.PuedeEditarAccesos(actor.Rol, actor.Id, UsuarioClaimsFactory.PerfilEfectivo(u, _opciones), u.Id),
            "No tienes permiso para cambiar los accesos de esta cuenta.");
        return u;
    }

    // ---------------------------------------------------------------- consultas

    public async Task<IReadOnlyList<UsuarioPanel>> UsuariosAsync(CancellationToken ct = default)
    {
        await using var db = Db();
        var usuarios = await db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync(ct);
        var empresas = await db.AccesosEmpresa.AsNoTracking().Where(a => a.Activo)
            .GroupBy(a => a.UsuarioId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var llaves = (await LlavesActivasAsync(db, null, ct)).ToLookup(x => x.UsuarioId, x => x.Llave);
        return usuarios.Select(u => Mapear(u, empresas.GetValueOrDefault(u.Id), llaves[u.Id])).ToList();
    }

    /// <summary>Llaves de las empresas activas (de un usuario o de todos), para deducir el nivel.</summary>
    private static async Task<List<(string UsuarioId, string Llave)>> LlavesActivasAsync(PlataformaDbContext db, string? usuarioId, CancellationToken ct)
    {
        var q = from l in db.AccesosLlave.AsNoTracking()
                join a in db.AccesosEmpresa.AsNoTracking() on new { l.UsuarioId, l.Ruc } equals new { a.UsuarioId, a.Ruc }
                where a.Activo && (usuarioId == null || l.UsuarioId == usuarioId)
                select new { l.UsuarioId, l.Llave };
        return (await q.Distinct().ToListAsync(ct)).Select(x => (x.UsuarioId, x.Llave)).ToList();
    }

    public async Task<UsuarioPanel?> UsuarioAsync(string usuarioId, CancellationToken ct = default)
    {
        await using var db = Db();
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == usuarioId, ct);
        if (u is null) return null;
        var n = await db.AccesosEmpresa.CountAsync(a => a.UsuarioId == usuarioId && a.Activo, ct);
        var llaves = (await LlavesActivasAsync(db, usuarioId, ct)).Select(x => x.Llave);
        return Mapear(u, n, llaves);
    }

    private UsuarioPanel Mapear(UsuarioApp u, int empresas, IEnumerable<string> llaves)
    {
        var perfil = UsuarioClaimsFactory.PerfilEfectivo(u, _opciones);
        return new(
            u.Id, u.UserName ?? "", u.NombreCompleto, u.Email, u.EmailConfirmed, u.Perfil, perfil,
            u.Activo, u.TwoFactorEnabled, u.TwoFactorEnabled ? (u.MetodoSegundoFactor ?? MetodosSegundoFactor.Totp) : null,
            u.PasswordHash is not null, u.UltimoAccesoUtc, empresas, u.PeachUsername, NivelesWeb.Describir(perfil, llaves));
    }

    /// <summary>Todas las empresas del catálogo (más las asignadas que ya no estén en él) con el estado de acceso del usuario.</summary>
    public async Task<IReadOnlyList<AccesoEmpresaPanel>> FichaAsync(string usuarioId, CancellationToken ct = default)
    {
        await using var db = Db();
        var accesos = await db.AccesosEmpresa.AsNoTracking().Include(a => a.Llaves)
            .Where(a => a.UsuarioId == usuarioId).ToListAsync(ct);
        var porRuc = accesos.ToDictionary(a => a.Ruc);
        var catalogo = await _empresas.TodasAsync(ct);

        var filas = catalogo.Select(e =>
        {
            porRuc.TryGetValue(e.Ruc, out var a);
            return new AccesoEmpresaPanel(e.Ruc, e.Nombre, e.Activa, a is not null, a?.Activo ?? false, a?.UsuarioSage,
                (a?.Llaves.Select(l => l.Llave) ?? Enumerable.Empty<string>()).ToHashSet(StringComparer.Ordinal), a?.ModificadoPor, a?.ModificadoUtc);
        }).ToList();
        foreach (var a in accesos.Where(a => catalogo.All(e => e.Ruc != a.Ruc)))
        {
            filas.Add(new AccesoEmpresaPanel(a.Ruc, a.Ruc, false, true, a.Activo, a.UsuarioSage,
                a.Llaves.Select(l => l.Llave).ToHashSet(StringComparer.Ordinal), a.ModificadoPor, a.ModificadoUtc));
        }

        // Asignadas primero, después las activas en PeachEBills, después el resto.
        return filas
            .OrderByDescending(f => f.Asignada)
            .ThenByDescending(f => f.EmpresaActiva)
            .ThenBy(f => f.Nombre, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ---------------------------------------------------------------- accesos

    /// <summary>
    /// Deja la empresa del usuario exactamente así (acceso activo o no, usuario de Sage y llaves) y audita cada diferencia.
    /// Llaves desconocidas se rechazan. Una empresa sin acceso conserva sus llaves (para reactivarla tal cual).
    /// </summary>
    public async Task GuardarEmpresaAsync(
        Actor actor, string usuarioId, string ruc, bool activo, string? usuarioSage, IEnumerable<string> llaves, CancellationToken ct = default)
    {
        var objetivo = await ObjetivoEditableAsync(actor, usuarioId);
        ruc = (ruc ?? "").Trim();
        Exigir(ruc.Length is >= 10 and <= 13 && ruc.All(char.IsAsciiDigit), "RUC inválido.");
        var nuevas = llaves.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToHashSet(StringComparer.Ordinal);
        var desconocidas = nuevas.Where(l => !LlavesWeb.Existe(l)).ToList();
        Exigir(desconocidas.Count == 0, "Llaves desconocidas: " + string.Join(", ", desconocidas));
        usuarioSage = string.IsNullOrWhiteSpace(usuarioSage) ? null : usuarioSage.Trim();
        Exigir(usuarioSage is null || usuarioSage.Length <= 50, "El usuario de Sage tiene más de 50 caracteres.");

        await using var db = Db();
        var acceso = await db.AccesosEmpresa.Include(a => a.Llaves).FirstOrDefaultAsync(a => a.UsuarioId == usuarioId && a.Ruc == ruc, ct);
        var cambios = new List<(string Tipo, string Detalle)>();
        var quien = objetivo.UserName;

        if (acceso is null)
        {
            if (!activo && nuevas.Count == 0 && usuarioSage is null) return; // nada que crear
            acceso = new AccesoEmpresa { UsuarioId = usuarioId, Ruc = ruc, Activo = activo, UsuarioSage = usuarioSage };
            db.AccesosEmpresa.Add(acceso);
            cambios.Add((activo ? TiposEventoAuth.EmpresaActivada : TiposEventoAuth.EmpresaDesactivada, $"{quien} · {ruc}"));
            if (usuarioSage is not null) cambios.Add((TiposEventoAuth.UsuarioSageCambiado, $"{quien} · {ruc} · Sage: {usuarioSage}"));
        }
        else
        {
            if (acceso.Activo != activo)
            {
                cambios.Add((activo ? TiposEventoAuth.EmpresaActivada : TiposEventoAuth.EmpresaDesactivada, $"{quien} · {ruc}"));
                acceso.Activo = activo;
            }
            if (!string.Equals(acceso.UsuarioSage, usuarioSage, StringComparison.Ordinal))
            {
                cambios.Add((TiposEventoAuth.UsuarioSageCambiado, $"{quien} · {ruc} · Sage: {acceso.UsuarioSage ?? "—"} → {usuarioSage ?? "—"}"));
                acceso.UsuarioSage = usuarioSage;
            }
        }

        var actuales = acceso.Llaves.Select(l => l.Llave).ToHashSet(StringComparer.Ordinal);
        var agregar = nuevas.Except(actuales).OrderBy(x => x).ToList();
        var quitar = actuales.Except(nuevas).OrderBy(x => x).ToList();
        foreach (var l in agregar)
        {
            acceso.Llaves.Add(new AccesoLlave { UsuarioId = usuarioId, Ruc = ruc, Llave = l, OtorgadoPor = actor.UserName });
        }
        acceso.Llaves.RemoveAll(l => quitar.Contains(l.Llave));
        if (agregar.Count > 0) cambios.Add((TiposEventoAuth.AccesoOtorgado, $"{quien} · {ruc} · +{string.Join(" +", agregar)}"));
        if (quitar.Count > 0) cambios.Add((TiposEventoAuth.AccesoQuitado, $"{quien} · {ruc} · -{string.Join(" -", quitar)}"));

        if (cambios.Count == 0) return;
        acceso.ModificadoPor = actor.UserName;
        acceso.ModificadoUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        foreach (var (tipo, detalle) in cambios)
        {
            await _auditoria.RegistrarAsync(tipo, actor.UserName, detalle, ct);
        }
    }

    /// <summary>
    /// Suma a la cuenta web las empresas y llaves que hoy tiene un usuario del .exe en PeachEBills (solo las llaves que la web conoce,
    /// y solo empresas activas). No quita nada. Devuelve cuántas empresas tocó.
    /// </summary>
    public async Task<int> ImportarDesdePeachEbillsAsync(Actor actor, string usuarioId, string peachUsername, CancellationToken ct = default)
    {
        await ObjetivoEditableAsync(actor, usuarioId);
        var empresasPeach = await _peachEbills.EmpresasDelUsuarioAsync(peachUsername, ct);
        var activas = (await _empresas.TodasAsync(ct)).Where(e => e.Activa).Select(e => e.Ruc).ToHashSet();
        var ficha = (await FichaAsync(usuarioId, ct)).ToDictionary(f => f.Ruc);
        var tocadas = 0;
        foreach (var e in empresasPeach.Where(e => activas.Contains(e.Ruc)))
        {
            var llaves = (await _peachEbills.PermisosAsync(peachUsername, e.Ruc, ct)).Where(LlavesWeb.Existe).ToHashSet();
            ficha.TryGetValue(e.Ruc, out var actual);
            var union = llaves.Union(actual?.Llaves ?? new HashSet<string>()).ToList();
            await GuardarEmpresaAsync(actor, usuarioId, e.Ruc, true, actual?.UsuarioSage, union, ct);
            tocadas++;
        }
        return tocadas;
    }

    public async Task<TokenExtensionInfo?> TokenExtensionAsync(string usuarioId, CancellationToken ct = default)
    {
        await using var ambito = Ambito();
        return await ambito.ServiceProvider.GetRequiredService<IServicioTokensExtension>().ObtenerInfoAsync(usuarioId, ct);
    }

    public async Task RevocarTokenExtensionAsync(Actor actor, string usuarioId, CancellationToken ct = default)
    {
        var u = await ObjetivoEditableAsync(actor, usuarioId);
        await using var ambito = Ambito();
        await ambito.ServiceProvider.GetRequiredService<IServicioTokensExtension>().RevocarAsync(usuarioId, ct);
        await _auditoria.RegistrarAsync(TiposEventoAuth.TokenExtensionRevocado, actor.UserName, u.UserName, ct);
    }

    // ---------------------------------------------------------------- usuarios (solo Super Admin)

    /// <summary>
    /// Alta de una cuenta personal SIN clave: la persona la define desde la invitación que le llega al correo (que así queda confirmado).
    /// </summary>
    public async Task<UsuarioApp> CrearUsuarioAsync(
        Actor actor, string usuario, string nombre, string email, string perfil, string? peachUsername, CancellationToken ct = default)
    {
        Exigir(ReglasPanel.PuedeAdministrarUsuarios(actor.Rol), "Solo un Super Admin crea usuarios.");
        usuario = (usuario ?? "").Trim();
        email = (email ?? "").Trim();
        Exigir(usuario.Length is >= 3 and <= 50, "El usuario interno debe tener entre 3 y 50 caracteres.");
        Exigir(EsCorreo(email), "El correo no es válido.");
        Exigir(Perfiles.Valido(perfil), "Perfil inválido.");

        await using var ambito = Ambito();
        var users = Usuarios(ambito);
        Exigir(await users.FindByNameAsync(usuario) is null, "Ya existe una cuenta con ese usuario interno.");
        Exigir(await users.FindByEmailAsync(email) is null, "Ya existe una cuenta con ese correo.");

        var nuevo = new UsuarioApp
        {
            UserName = usuario,
            Email = email,
            EmailConfirmed = false,
            NombreCompleto = string.IsNullOrWhiteSpace(nombre) ? usuario : nombre.Trim(),
            PeachUsername = string.IsNullOrWhiteSpace(peachUsername) ? usuario : peachUsername.Trim(),
            Perfil = perfil,
            Activo = true,
        };
        var r = await users.CreateAsync(nuevo);
        Exigir(r.Succeeded, "No se pudo crear: " + string.Join("; ", r.Errors.Select(e => e.Description)));
        await _auditoria.RegistrarAsync(TiposEventoAuth.AdminAltaUsuario, actor.UserName, $"creó {usuario} ({Perfiles.Etiqueta(perfil)}, {email})", ct);
        return nuevo;
    }

    /// <summary>Manda (o reenvía) el enlace para definir la clave. Sirve de invitación y de reseteo hecho por un Super Admin.</summary>
    public async Task EnviarInvitacionAsync(Actor actor, string usuarioId, string urlBase, CancellationToken ct = default)
    {
        Exigir(ReglasPanel.PuedeAdministrarUsuarios(actor.Rol), "Solo un Super Admin envía invitaciones.");
        Exigir(_correo.Disponible, "El correo saliente no está configurado.");
        await using var ambito = Ambito();
        var users = Usuarios(ambito);
        var u = await users.FindByIdAsync(usuarioId) ?? throw new InvalidOperationException("No existe ese usuario.");
        Exigir(!string.IsNullOrWhiteSpace(u.Email), "La cuenta no tiene correo.");
        var enlace = await EnlaceClaveAsync(users, u, urlBase, "activar-cuenta");
        var (asunto, html) = MensajesCuenta.Invitacion(u.NombreCompleto ?? u.UserName!, u.UserName!, enlace);
        await _correo.EnviarAsync(u.Email!, asunto, html, ct);
        await _auditoria.RegistrarAsync(TiposEventoAuth.InvitacionEnviada, actor.UserName, $"{u.UserName} → {u.Email}", ct);
    }

    /// <summary>
    /// Invitación a todas las cuentas activas con correo que todavía no tienen clave (p. ej. las creadas por la siembra de AW-4).
    /// Devuelve a cuántas se envió y los errores por cuenta.
    /// </summary>
    public async Task<(int Enviadas, IReadOnlyList<string> Errores)> EnviarInvitacionesPendientesAsync(Actor actor, string urlBase, CancellationToken ct = default)
    {
        Exigir(ReglasPanel.PuedeAdministrarUsuarios(actor.Rol), "Solo un Super Admin envía invitaciones.");
        Exigir(_correo.Disponible, "El correo saliente no está configurado.");
        List<string> pendientes;
        await using (var db = Db())
        {
            pendientes = await db.Users.AsNoTracking()
                .Where(u => u.Activo && u.PasswordHash == null && u.Email != null && u.Email != "")
                .Select(u => u.Id).ToListAsync(ct);
        }
        var errores = new List<string>();
        var enviadas = 0;
        foreach (var id in pendientes)
        {
            try
            {
                await EnviarInvitacionAsync(actor, id, urlBase, ct);
                enviadas++;
            }
            catch (Exception ex)
            {
                errores.Add($"{(await LeerAsync(id, ct))?.UserName ?? id}: {ex.Message}");
            }
        }
        return (enviadas, errores);
    }

    /// <summary>Recuperación pedida por la propia persona. No revela si el correo existe: siempre "termina bien".</summary>
    public async Task SolicitarRecuperacionAsync(string email, string urlBase, CancellationToken ct = default)
    {
        if (!EsCorreo(email) || !_correo.Disponible) return;
        await using var ambito = Ambito();
        var users = Usuarios(ambito);
        var u = await users.FindByEmailAsync(email.Trim());
        if (u is null || !u.Activo) return;
        var enlace = await EnlaceClaveAsync(users, u, urlBase, "restablecer-clave");
        var (asunto, html) = MensajesCuenta.RecuperarClave(u.NombreCompleto ?? u.UserName!, enlace);
        await _correo.EnviarAsync(u.Email!, asunto, html, ct);
        await _auditoria.RegistrarAsync(TiposEventoAuth.ClaveRecuperada, u.UserName, "enlace enviado", ct);
    }

    /// <summary>Define la clave con el enlace (invitación o recuperación). Deja el correo confirmado y desbloquea la cuenta.</summary>
    public async Task<(bool Ok, string? Error)> DefinirClaveAsync(string usuarioId, string token, string clave, CancellationToken ct = default)
    {
        await using var ambito = Ambito();
        var users = Usuarios(ambito);
        var u = await users.FindByIdAsync(usuarioId ?? "");
        if (u is null || !u.Activo) return (false, "El enlace no es válido o venció. Pide uno nuevo.");
        var r = await users.ResetPasswordAsync(u, token ?? "", clave ?? "");
        if (!r.Succeeded)
        {
            var tokenMalo = r.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken));
            return (false, tokenMalo ? "El enlace no es válido o venció. Pide uno nuevo." : string.Join(" ", r.Errors.Select(e => e.Description)));
        }
        u.EmailConfirmed = true;
        await users.UpdateAsync(u);
        await users.SetLockoutEndDateAsync(u, null);
        await users.ResetAccessFailedCountAsync(u);
        await _auditoria.RegistrarAsync(TiposEventoAuth.CuentaActivada, u.UserName, "clave definida por enlace", ct);
        return (true, null);
    }

    private static async Task<string> EnlaceClaveAsync(UserManager<UsuarioApp> users, UsuarioApp u, string urlBase, string ruta)
    {
        var token = await users.GeneratePasswordResetTokenAsync(u);
        return $"{urlBase.TrimEnd('/')}/{ruta}?u={Uri.EscapeDataString(u.Id)}&t={Uri.EscapeDataString(token)}";
    }

    /// <summary>Clave temporal puesta por un Super Admin (cuando el correo no llega). Devuelve los errores de la política, si los hay.</summary>
    public async Task<string?> PonerClaveTemporalAsync(Actor actor, string usuarioId, string clave, CancellationToken ct = default)
    {
        Exigir(ReglasPanel.PuedeAdministrarUsuarios(actor.Rol), "Solo un Super Admin pone claves temporales.");
        await using var ambito = Ambito();
        var users = Usuarios(ambito);
        var u = await users.FindByIdAsync(usuarioId) ?? throw new InvalidOperationException("No existe ese usuario.");
        foreach (var v in users.PasswordValidators)
        {
            var val = await v.ValidateAsync(users, u, clave ?? "");
            if (!val.Succeeded) return string.Join(" ", val.Errors.Select(e => e.Description));
        }
        if (await users.HasPasswordAsync(u)) await users.RemovePasswordAsync(u);
        var r = await users.AddPasswordAsync(u, clave ?? "");
        if (!r.Succeeded) return string.Join(" ", r.Errors.Select(e => e.Description));
        await _auditoria.RegistrarAsync(TiposEventoAuth.AdminResetClave, actor.UserName, $"clave temporal para {u.UserName}", ct);
        return null;
    }

    public async Task QuitarSegundoFactorAsync(Actor actor, string usuarioId, CancellationToken ct = default)
    {
        Exigir(ReglasPanel.PuedeAdministrarUsuarios(actor.Rol), "Solo un Super Admin quita la verificación en dos pasos.");
        await using var ambito = Ambito();
        var users = Usuarios(ambito);
        var u = await users.FindByIdAsync(usuarioId) ?? throw new InvalidOperationException("No existe ese usuario.");
        await ambito.ServiceProvider.GetRequiredService<GestorSegundoFactor>().DesactivarAsync(u);
        await _auditoria.RegistrarAsync(TiposEventoAuth.Admin2faQuitado, actor.UserName, u.UserName, ct);
    }

    public async Task CambiarPerfilAsync(Actor actor, string usuarioId, string perfil, CancellationToken ct = default)
    {
        Exigir(ReglasPanel.PuedeCambiarPerfil(actor.Rol, actor.Id, usuarioId), "No puedes cambiar el perfil de esta cuenta.");
        Exigir(Perfiles.Valido(perfil), "Perfil inválido.");
        await using var ambito = Ambito();
        var users = Usuarios(ambito);
        var u = await users.FindByIdAsync(usuarioId) ?? throw new InvalidOperationException("No existe ese usuario.");
        if (u.Perfil == perfil) return;
        var antes = u.Perfil;
        u.Perfil = perfil;
        await users.UpdateAsync(u);
        await users.UpdateSecurityStampAsync(u); // la sesión abierta de esa persona se re-valida con el perfil nuevo
        await _auditoria.RegistrarAsync(TiposEventoAuth.PerfilCambiado, actor.UserName,
            $"{u.UserName}: {Perfiles.Etiqueta(antes)} → {Perfiles.Etiqueta(perfil)}", ct);
    }

    public async Task CambiarCorreoAsync(Actor actor, string usuarioId, string email, CancellationToken ct = default)
    {
        Exigir(ReglasPanel.PuedeAdministrarUsuarios(actor.Rol), "Solo un Super Admin cambia correos.");
        email = (email ?? "").Trim();
        Exigir(EsCorreo(email), "El correo no es válido.");
        await using var ambito = Ambito();
        var users = Usuarios(ambito);
        var u = await users.FindByIdAsync(usuarioId) ?? throw new InvalidOperationException("No existe ese usuario.");
        if (string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)) return;
        var otro = await users.FindByEmailAsync(email);
        Exigir(otro is null || otro.Id == u.Id, "Ya existe una cuenta con ese correo.");
        var antes = u.Email;
        await users.SetEmailAsync(u, email); // deja EmailConfirmed=false y cambia el sello de seguridad
        await _auditoria.RegistrarAsync(TiposEventoAuth.PerfilCambiado, actor.UserName, $"{u.UserName}: correo {antes ?? "—"} → {email}", ct);
    }

    public async Task CambiarActivoAsync(Actor actor, string usuarioId, bool activo, CancellationToken ct = default)
    {
        Exigir(ReglasPanel.PuedeAdministrarUsuarios(actor.Rol), "Solo un Super Admin habilita o deshabilita cuentas.");
        Exigir(actor.Id != usuarioId, "No puedes deshabilitar tu propia cuenta.");
        await using var ambito = Ambito();
        var users = Usuarios(ambito);
        var u = await users.FindByIdAsync(usuarioId) ?? throw new InvalidOperationException("No existe ese usuario.");
        if (u.Activo == activo) return;
        u.Activo = activo;
        await users.UpdateAsync(u);
        await users.UpdateSecurityStampAsync(u); // cierra sus sesiones al re-validar la cookie
        await _auditoria.RegistrarAsync(activo ? TiposEventoAuth.AdminHabilitado : TiposEventoAuth.AdminDeshabilitado, actor.UserName, u.UserName, ct);
    }

    public static bool EsCorreo(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length > 256) return false;
        try
        {
            var m = new System.Net.Mail.MailAddress(s.Trim());
            return m.Address == s.Trim() && m.Host.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
