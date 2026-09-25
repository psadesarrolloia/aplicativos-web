using System.Data.Odbc;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PsaWeb.Compras.Armado;
using PsaWeb.Compras.Bridge;
using PsaWeb.Compras.Catalogo;
using PsaWeb.Compras.Sage;
using PsaWeb.PeachEbills.Data;
using PsaWeb.Sage50;
using PsaWeb.SageBridge.Cola;
using PsaWeb.SageBridge.Cola.Data;
using PsaWeb.SageBridge.Contratos;
using PsaWeb.Seguridad;

namespace PsaWeb.Modules.Compras.Servicios;

/// <summary>Catálogos y listas de una empresa para el formulario (se cargan una vez por circuito).</summary>
public sealed record ContextoCompras(
    string Ruc,
    CatalogoCompras Catalogo,
    IReadOnlyList<CuentaSage> Cuentas,
    IReadOnlyList<JobSage> Jobs,
    string SerieRetencion,
    int SecuencialRetencionInicial);

/// <summary>Resultado de pulsar «Guardar».</summary>
public sealed record ResultadoGuardarCompra(ResultadoArmado Armado, long? TrabajoId, string? Error);

/// <summary>Permisos del usuario en el módulo (<c>qupurchinv</c>/<c>mkpurchinv</c>, §9 del plan).</summary>
public sealed record PermisosCompras(bool Ver, bool Registrar)
{
    public static readonly PermisosCompras Todos = new(true, true);
}

/// <summary>
/// Orquesta el módulo Compras: lee Sage por ODBC (empresa de sesión), arma la OC con <see cref="ArmadorOc"/>, la encola en el
/// Sage Bridge (<see cref="TiposTrabajo.GuardarOc"/>) y deja la auditoría. Scoped (un circuito).
/// </summary>
public sealed class ServicioCompras(
    ISageConnectionFactory conexiones,
    IResolverEmpresaSage resolver,
    IDbContextFactory<PeachEbillsContext> peachEbills,
    IColaSage cola,
    IAuditoriaSage auditoria,
    IServiceProvider servicios)
{
    public const string Modulo = "Compras";

    /// <summary>GateProvisional (§9): ver <see cref="ReglasCompras.PermisosProvisionales"/>.</summary>
    public static bool PermisosProvisionales
    {
        get => ReglasCompras.PermisosProvisionales;
        set => ReglasCompras.PermisosProvisionales = value;
    }

    private readonly Dictionary<string, ContextoCompras> _contextos = new();

    public async Task<PermisosCompras> PermisosAsync(string usuario, string ruc, CancellationToken ct = default)
    {
        if (servicios.GetService(typeof(ISecurityDirectory)) is not ISecurityDirectory dir) return PermisosCompras.Todos;
        var p = await dir.PermisosAsync(usuario, ruc, ct);
        return new PermisosCompras(ReglasCompras.PuedeVer(p), ReglasCompras.PuedeRegistrar(p));
    }

    public async Task<ContextoCompras> ContextoAsync(string ruc, CancellationToken ct = default)
    {
        if (_contextos.TryGetValue(ruc, out var c)) return c;
        await using var db = await peachEbills.CreateDbContextAsync(ct);
        var pe = await LectorCatalogoPeachEbills.LeerAsync(db, ct);
        var est = await db.Establishments.AsNoTracking()
            .Where(x => x.Ruc == ruc && x.IsElectronic && x.StartNumerationTwh != null)
            .OrderBy(x => x.EstablishmentId).FirstOrDefaultAsync(ct);
        await using var cn = await AbrirAsync(ruc, ct);
        c = new ContextoCompras(
            ruc,
            await LectorCatalogoCompras.LeerAsync(cn, pe, ct),
            await LectorOcs.CuentasAsync(cn, ct),
            await LectorOcs.JobsAsync(cn, ct),
            est is null ? "001-001" : $"{est.Code.Trim()}-{est.IssuePoint.Trim()}",
            est?.StartNumerationTwh ?? 0);
        _contextos[ruc] = c;
        return c;
    }

    public async Task<IReadOnlyList<OcResumen>> ListarAsync(string ruc, DateTime desde, DateTime hasta, TipoDocumentoCompra tipo, CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ruc, ct);
        return await LectorOcs.ListarAsync(cn, desde, hasta, tipo, ct);
    }

    /// <summary>OC guardada reconstruida como formulario, más su cabecera (estado, nº de OC).</summary>
    public async Task<(ResultadoRecarga Recarga, OcGuardadaCabecera Cabecera)?> AbrirAsync(string ruc, int postOrder, CancellationToken ct = default)
    {
        var contexto = await ContextoAsync(ruc, ct);
        await using var cn = await AbrirAsync(ruc, ct);
        var oc = await LectorOcs.LeerAsync(cn, postOrder, ct);
        if (oc is null) return null;
        var proveedor = await LectorCatalogoCompras.ProveedorPorIdAsync(cn, oc.Cabecera.VendorId, ct);
        if (proveedor is null) return null;
        return (RecargaOc.Reconstruir(oc, proveedor, contexto.Catalogo, ruc), oc.Cabecera);
    }

    public async Task<IReadOnlyList<ProveedorSage>> ProveedoresPorIdentificacionAsync(string ruc, string identificacion, CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ruc, ct);
        return await LectorCatalogoCompras.ProveedoresPorIdentificacionAsync(cn, identificacion.Trim(), ct);
    }

    public async Task<ProveedorSage?> ProveedorPorIdAsync(string ruc, string id, CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ruc, ct);
        return await LectorCatalogoCompras.ProveedorPorIdAsync(cn, id.Trim(), ct);
    }

    /// <summary>ID propuesto para un proveedor nuevo (§5.1) y si el escrito ya está usado.</summary>
    public async Task<(string Propuesto, bool Usado)> IdProveedorAsync(string ruc, string razonSocial, string? escrito, CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ruc, ct);
        var ids = await LectorCatalogoCompras.IdsProveedoresAsync(cn, ct);
        var usado = !string.IsNullOrWhiteSpace(escrito) && ids.Any(x => string.Equals(x, escrito.Trim(), StringComparison.OrdinalIgnoreCase));
        return (ReglasProveedor.IdPropuesto(razonSocial, ids), usado);
    }

    /// <summary>Vista previa de la numeración (el Bridge asigna la definitiva al guardar).</summary>
    public async Task<(string Prefijo, string ProximaOc, string ProximaRetencion)> NumeracionAsync(string ruc, CancellationToken ct = default)
    {
        var contexto = await ContextoAsync(ruc, ct);
        await using var cn = await AbrirAsync(ruc, ct);
        var prefijo = await LectorOcs.UltimoPrefijoAsync(cn, ct);
        return (prefijo, await LectorOcs.ProximaOcAsync(cn, prefijo, ct),
            await LectorOcs.ProximaRetencionAsync(cn, contexto.SerieRetencion, contexto.SecuencialRetencionInicial, ct));
    }

    /// <summary>Base de Sage (<c>dbq</c>) que se lee para la empresa, para mostrarla en pantalla (sin la cadena ni la clave).</summary>
    public async Task<string?> BaseSageAsync(string ruc, CancellationToken ct = default)
    {
        var cadena = await resolver.CadenaOdbcAsync(ruc, ct);
        if (cadena is null) return null;
        var parte = cadena.Split(';').FirstOrDefault(x => x.TrimStart().StartsWith("dbq=", StringComparison.OrdinalIgnoreCase)
                                                      || x.TrimStart().StartsWith("dsn=", StringComparison.OrdinalIgnoreCase));
        return parte?.Split('=', 2)[1].Trim();
    }

    /// <summary>¿La empresa está habilitada en el Sage Bridge? Si no, los trabajos quedarían en cola sin procesarse.</summary>
    public async Task<bool> BridgeHabilitadoAsync(string ruc, CancellationToken ct = default) =>
        (await cola.EmpresasAsync(ct)).Any(e => e.Ruc == ruc && e.Habilitada);

    /// <summary>
    /// Valida y arma la OC; si no hay errores (y el usuario confirmó lo que pide confirmación) la encola en el Bridge.
    /// Siempre deja auditoría: el encolado o el rechazo por validación.
    /// </summary>
    public async Task<ResultadoGuardarCompra> GuardarAsync(string ruc, string usuario, EntradaCompra entrada, bool numeroOcAutomatico,
        bool numeroRetencionAutomatico, bool confirmado, CancellationToken ct = default, string? huellaOrigen = null)
    {
        var contexto = await ContextoAsync(ruc, ct);
        var armado = ArmadorOc.Armar(entrada, contexto.Catalogo);
        if (armado.Oc is not { } oc)
        {
            await AuditarAsync(ruc, usuario, entrada, "RechazoValidacion", "Rechazado", null, null, string.Join(" | ", armado.Errores), huellaOrigen, ct);
            return new(armado, null, null);
        }
        if (armado.Confirmaciones.Count > 0 && !confirmado) return new(armado, null, null);
        if (!await BridgeHabilitadoAsync(ruc, ct))
        {
            return new(armado, null, "La empresa no está habilitada en el Sage Bridge (administración › Sage Bridge): la compra no se registraría.");
        }

        var solicitud = SolicitudGuardarOc.Crear(oc, entrada.Proveedor, numeroOcAutomatico, numeroRetencionAutomatico,
            contexto.SerieRetencion, contexto.SecuencialRetencionInicial);
        var encolado = await cola.EncolarAsync(ruc, solicitud.Tipo, solicitud.PayloadJson, solicitud.ClaveIdempotencia, usuario, ct);
        await AuditarAsync(ruc, usuario, entrada, TiposTrabajo.GuardarOc, encolado.Nuevo ? "Encolado" : "YaEncolado",
            encolado.Trabajo.Id, Huella(solicitud.PayloadJson), numeroOcAutomatico ? null : $"OC {oc.Referencia}", huellaOrigen, ct);
        return new(armado, encolado.Trabajo.Id, null);
    }

    public Task<TrabajoSage?> TrabajoAsync(long id, CancellationToken ct = default) => cola.ObtenerAsync(id, ct);

    public Task<IReadOnlyList<AuditoriaRegistroSage>> HistorialAsync(string ruc, string proveedorId, string factura, CancellationToken ct = default) =>
        auditoria.DelDocumentoAsync(ruc, proveedorId, factura, ct);

    private Task AuditarAsync(string ruc, string usuario, EntradaCompra e, string accion, string resultado, long? trabajo,
        string? huella, string? detalle, string? huellaOrigen, CancellationToken ct) =>
        auditoria.RegistrarAsync(new AuditoriaRegistroSage
        {
            Ruc = ruc,
            Modulo = Modulo,
            Accion = accion,
            Usuario = usuario,
            Documento = e.NumeroFactura,
            Tercero = e.Proveedor.Id,
            Referencia = e.Autorizacion,
            TrabajoId = trabajo,
            Resultado = resultado,
            Detalle = detalle,
            HuellaPayload = huella,
            HuellaOrigen = huellaOrigen,
        }, ct);

    private static string Huella(string texto) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));

    /// <summary>Conexión ODBC (solo lectura) a la empresa, para los servicios del módulo.</summary>
    internal Task<OdbcConnection> ConexionAsync(string ruc, CancellationToken ct) => AbrirAsync(ruc, ct);

    private async Task<OdbcConnection> AbrirAsync(string ruc, CancellationToken ct)
    {
        var cadena = await resolver.CadenaOdbcAsync(ruc, ct);
        var cn = cadena is null ? conexiones.CreateConnection() : conexiones.CreateConnection(cadena);
        await cn.OpenAsync(ct);
        return cn;
    }
}
