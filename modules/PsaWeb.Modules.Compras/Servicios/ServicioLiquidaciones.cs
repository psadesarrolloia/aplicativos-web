using System.Data.Odbc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PsaWeb.Compras.Importaciones;
using PsaWeb.PeachEbills.Data;
using PsaWeb.Sage50;
using PsaWeb.SageBridge.Cola;
using PsaWeb.SageBridge.Cola.Data;
using PsaWeb.SageBridge.Contratos;
using PsaWeb.Seguridad;

namespace PsaWeb.Modules.Compras.Servicios;

/// <summary>Permisos del usuario en la liquidación de importaciones (<c>quimpliq</c>/<c>mkimpliq</c>).</summary>
/// <param name="UsuarioSage">Usuario de Sage 50 vinculado a la cuenta en la empresa (fuente de accesos Web; PLAN-ACCESOS-WEB §6).</param>
/// <param name="FaltaUsuarioSage">Tiene la llave de registrar pero su cuenta no tiene usuario de Sage en la empresa.</param>
public sealed record PermisosLiquidacion(bool Ver, bool Registrar, string? UsuarioSage = null, bool FaltaUsuarioSage = false)
{
    public static readonly PermisosLiquidacion Todos = new(true, true);

    public string? MotivoSinRegistro => Registrar ? null : FaltaUsuarioSage ? MensajesEscrituraSage.SinUsuarioSage : MensajesEscrituraSage.SinPermiso;
}

/// <summary>Una fila de la lista de importaciones.</summary>
public sealed record FilaImportacion(CuentaImportacion Cuenta, EstadoImportacion Estado, string? Proveedor, string? ReferenciaOc);

/// <summary>Todo lo que necesita el formulario de una importación.</summary>
public sealed class DetalleImportacion
{
    public required CuentaImportacion Cuenta { get; init; }
    public required EstadoImportacion Estado { get; init; }

    /// <summary>Filas de la cuenta (de Sage) con la marca de factura conciliada (C2).</summary>
    public required List<GastoImportacion> Gastos { get; init; }

    /// <summary>Filas de la liquidación guardada que ya no están en Sage (C2: se avisa).</summary>
    public required List<GastoImportacion> GastosDesaparecidos { get; init; }

    public required List<ItemLiquidacion> Items { get; init; }
    public int? LiquidacionId { get; init; }
    public string ProveedorId { get; init; } = string.Empty;
    public string Prefijo { get; init; } = Liquidaciones.PrefijoPorDefecto;
    public string Numero { get; init; } = string.Empty;
    public DateTime Fecha { get; init; } = DateTime.Today;

    /// <summary>La OC de la liquidación en Sage (si existe) y su compra.</summary>
    public OcLiquidacion? Oc { get; init; }

    /// <summary>Editable: sin OC, o con OC aún sin compra (se actualiza en el lugar).</summary>
    public bool Editable => EstadosImportacion.Editable(Estado) || (Estado == EstadoImportacion.EnProceso && Oc is { PostOrderCompra: null });

    /// <summary>
    /// Filas sin la línea LIQUIDACION de la compra de la liquidación (la que salda la cuenta). B6: el `.exe`, ya liquidada, la metía en
    /// el reporte como un gasto más; el reporte y el control de costeo usan esta lista.
    /// </summary>
    public IReadOnlyList<GastoImportacion> GastosSinLiquidacion(IEnumerable<GastoImportacion> gastos) =>
        Oc?.ReferenciaCompra is { } r
            ? gastos.Where(x => !(x.Referencia == r && x.Descripcion == "LIQUIDACION")).ToList()
            : gastos.ToList();

    /// <summary>
    /// Por qué no se puede sacar el reporte (null = se puede), como <c>btnReport_Click</c>: hace falta la liquidación guardada y, salvo que
    /// ya esté liquidada (el `.exe` no lo exigía en ese estado), el prorrateo completo.
    /// </summary>
    public string? MotivoSinReporte(IReadOnlyList<ItemLiquidacion> items, IEnumerable<GastoImportacion> gastos)
    {
        if (LiquidacionId is null) return "Guarda primero la liquidación.";
        if (items.Count == 0) return "Ingresa ítems a prorratear.";
        if (Estado != EstadoImportacion.Liquidada && !Liquidaciones.CosteoCompleto(items, GastosSinLiquidacion(gastos)))
        {
            return "Completa primero el proceso de prorrateo (el costo total de los ítems debe cuadrar con la importación).";
        }
        return null;
    }

    /// <summary>Avisos al abrir (vínculo recuperado, OC borrada en Sage, filas nuevas/desaparecidas).</summary>
    public List<string> Avisos { get; } = new();
}

/// <summary>Catálogos de Sage de una empresa para el formulario (una vez por circuito).</summary>
/// <summary>
/// Catálogos y convenciones de la empresa: cuenta por pagar (C6), si la compra lleva la referencia de la OC con espacio (CPTDC) o igual
/// (SANCEV), y la referencia de su última liquidación (para proponer la siguiente).
/// </summary>
public sealed record CatalogoLiquidacion(IReadOnlyDictionary<string, ItemStock> Items, IReadOnlyList<ProveedorLiquidacion> Proveedores,
    string CuentaPorPagar, bool CompraConEspacio, string? UltimaReferencia)
{
    /// <summary>Referencia de la compra de una OC según la convención de la empresa.</summary>
    public string ReferenciaCompra(string referenciaOc) => CompraConEspacio ? ReferenciasLiquidacion.DeCompra(referenciaOc) : referenciaOc;
}

/// <summary>
/// Orquesta la liquidación de importaciones (docs/PLAN-OLA2-LIQUIDACION-IMPORTACIONES.md §4–§5): lee Sage por ODBC, guarda en
/// PeachEBills (en el lugar, C3), encola la OC (<see cref="TiposTrabajo.GuardarOcLiquidacion"/>) y la compra
/// (<see cref="TiposTrabajo.ConvertirLiquidacion"/>) en el Sage Bridge y deja la auditoría. Scoped (un circuito).
/// </summary>
public sealed class ServicioLiquidaciones(
    ISageConnectionFactory conexiones,
    IResolverEmpresaSage resolver,
    IDbContextFactory<PeachEbillsContext> peachEbills,
    IColaSage cola,
    IAuditoriaSage auditoria,
    IServiceProvider servicios)
{
    public const string Modulo = "LiquidacionImportaciones";

    private readonly Dictionary<string, CatalogoLiquidacion> _catalogos = new();

    public async Task<PermisosLiquidacion> PermisosAsync(string usuario, string ruc, CancellationToken ct = default)
    {
        if (servicios.GetService(typeof(ISecurityDirectory)) is not ISecurityDirectory dir) return PermisosLiquidacion.Todos;
        var p = await dir.PermisosAsync(usuario, ruc, ct);
        var vinculo = await dir.VinculoSageAsync(usuario, ruc, ct);
        var llave = ReglasCompras.PuedeRegistrarLiquidaciones(p);
        return new PermisosLiquidacion(ReglasCompras.PuedeVerLiquidaciones(p), llave && vinculo.PermiteEscribir, vinculo.UsuarioSage,
            FaltaUsuarioSage: llave && !vinculo.PermiteEscribir);
    }

    /// <summary>Revalidación en el servidor antes de guardar o encolar: devuelve el motivo si no puede, y el usuario para la auditoría.</summary>
    private async Task<(string? Motivo, string Auditado)> ExigirRegistroAsync(string usuario, string ruc, CancellationToken ct)
    {
        var permisos = await PermisosAsync(usuario, ruc, ct);
        return (permisos.MotivoSinRegistro, MensajesEscrituraSage.ConUsuarioSage(usuario, permisos.UsuarioSage));
    }

    public async Task<CatalogoLiquidacion> CatalogoAsync(string ruc, CancellationToken ct = default)
    {
        if (_catalogos.TryGetValue(ruc, out var c)) return c;
        await using var cn = await AbrirAsync(ruc, ct);
        var items = await LectorImportaciones.ItemsStockAsync(cn, ct);
        await using var db = await peachEbills.CreateDbContextAsync(ct);
        c = new CatalogoLiquidacion(
            items.GroupBy(x => x.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
            await LectorImportaciones.ProveedoresAsync(cn, ct),
            await LectorImportaciones.CuentaPorPagarAsync(cn, ct) ?? PsaWeb.Compras.Armado.ArmadorOc.CuentaPorPagarPorDefecto,
            await LectorImportaciones.CompraConEspacioAsync(cn, ct),
            await RepositorioLiquidaciones.UltimaReferenciaAsync(db, ruc, ct));
        _catalogos[ruc] = c;
        return c;
    }

    /// <summary>Cuentas de importación con su estado (§1.7).</summary>
    public async Task<IReadOnlyList<FilaImportacion>> ListarAsync(string ruc, CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ruc, ct);
        var cuentas = await LectorImportaciones.CuentasAsync(cn, ct);
        await using var db = await peachEbills.CreateDbContextAsync(ct);
        var guardadas = await RepositorioLiquidaciones.ResumenAsync(db, ruc, ct);
        var ocs = await LectorImportaciones.OcsExistentesAsync(cn, guardadas.Values.Where(x => x.PostOrder is not null).Select(x => x.PostOrder!.Value).ToList(), ct);
        return cuentas.Select(c =>
        {
            var g = guardadas.TryGetValue(c.Cuenta, out var x) ? x : default;
            var guardada = guardadas.ContainsKey(c.Cuenta);
            var estado = EstadosImportacion.Calcular(c.Movimientos, c.Saldo, guardada, g.PostOrder is { } po && ocs.Contains(po));
            return new FilaImportacion(c, estado, g.Proveedor, g.Referencia);
        }).ToList();
    }

    /// <summary>
    /// Abre una importación (constructor de <c>sageImportMgm</c> + <c>updateObjects</c>): filas de Sage conciliadas con lo guardado (C2),
    /// ítems guardados, y si la OC sigue en Sage, proveedor/referencia/fecha salen de ella. Si la liquidación guardada no alcanzó a
    /// anotar la OC, la busca por referencia y proveedor y anota el vínculo.
    /// </summary>
    public async Task<DetalleImportacion?> AbrirAsync(string ruc, string cuenta, CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ruc, ct);
        var c = await LectorImportaciones.CuentaAsync(cn, cuenta, ct);
        if (c is null) return null;
        var deSage = await LectorImportaciones.GastosAsync(cn, cuenta, ct);
        await using var db = await peachEbills.CreateDbContextAsync(ct);
        var g = await RepositorioLiquidaciones.UltimaAsync(db, ruc, cuenta, ct);
        var avisos = new List<string>();

        OcLiquidacion? oc = null;
        if (g?.PostOrder is { } po)
        {
            oc = await LectorImportaciones.OcAsync(cn, po, ct);
            if (oc is null) avisos.Add($"La OC {g.ReferenciaOc} (PostOrder {po}) ya no está en Sage: la liquidación vuelve a «En Tránsito *».");
        }
        else if (g is { ReferenciaOc: { Length: > 0 } r, ProveedorId: { Length: > 0 } prov })
        {
            oc = await LectorImportaciones.OcPorReferenciaAsync(cn, r, prov, ct);
            if (oc is not null)
            {
                await RepositorioLiquidaciones.AnotarOcAsync(db, g.Id, oc.PostOrder, null, oc.Referencia, oc.Fecha, ct);
                avisos.Add($"Se encontró en Sage la OC {oc.Referencia} de esta liquidación y quedó vinculada.");
            }
        }

        var (gastos, desaparecidos) = Liquidaciones.Conciliar(deSage, g?.Gastos);
        if (g is not null && gastos.Any(x => x.Nueva)) avisos.Add($"Hay {gastos.Count(x => x.Nueva)} fila(s) nuevas en la cuenta desde que se guardó la liquidación.");
        if (desaparecidos.Count > 0) avisos.Add($"{desaparecidos.Count} fila(s) de la liquidación guardada ya no están en la cuenta de Sage.");

        var estado = EstadosImportacion.Calcular(c.Movimientos, c.Saldo, g is not null, oc is not null);
        var (prefijo, numero) = oc is not null ? Liquidaciones.Separar(oc.Referencia)
            : g?.ReferenciaOc is { Length: > 0 } rg ? Liquidaciones.Separar(rg)
            : Liquidaciones.NumeroPropuesto(c.Descripcion) is { } propuesto
                ? Liquidaciones.Separar(Liquidaciones.ReferenciaPropuesta((await CatalogoAsync(ruc, ct)).UltimaReferencia, propuesto))
                : (Liquidaciones.PrefijoPorDefecto, string.Empty);
        var detalle = new DetalleImportacion
        {
            Cuenta = c,
            Estado = estado,
            Gastos = gastos,
            GastosDesaparecidos = desaparecidos,
            Items = g?.Items ?? [],
            LiquidacionId = g?.Id,
            ProveedorId = oc?.ProveedorId ?? g?.ProveedorId ?? string.Empty,
            Prefijo = prefijo,
            Numero = numero,
            Fecha = oc?.Fecha ?? g?.Fecha ?? DateTime.Today,
            Oc = oc,
        };
        detalle.Avisos.AddRange(avisos);
        return detalle;
    }

    /// <summary>«Guardar» (<c>SaveChangesOnDB</c>, C3 en el lugar). Exige proveedor válido y referencia, como el `.exe`.</summary>
    public async Task<(int? Id, List<string> Errores)> GuardarAsync(string ruc, string usuario, string cuenta, string proveedorId, string referencia,
        DateTime fecha, IReadOnlyList<ItemLiquidacion> items, IReadOnlyList<GastoImportacion> gastos, CancellationToken ct = default)
    {
        var (motivo, auditado) = await ExigirRegistroAsync(usuario, ruc, ct);
        if (motivo is not null) return (null, [motivo]);
        var errores = await ValidarCabeceraAsync(ruc, proveedorId, referencia, ct);
        if (errores.Count > 0) return (null, errores);
        await using var db = await peachEbills.CreateDbContextAsync(ct);
        var id = await RepositorioLiquidaciones.GuardarAsync(db, ruc, cuenta, new DatosLiquidacion(proveedorId.Trim(), referencia.Trim(), fecha, items, gastos), ct);
        await AuditarAsync(ruc, auditado, cuenta, proveedorId, referencia, "Guardar", "Guardado", null, null, $"{items.Count} ítems, {gastos.Count} filas", ct);
        return (id, errores);
    }

    /// <summary>
    /// «Crear OC» (<c>btnLoad_Click</c>): valida, guarda la liquidación y encola <see cref="TiposTrabajo.GuardarOcLiquidacion"/> (crea, o
    /// actualiza en el lugar si ya hay OC sin compra). El vínculo se anota con <see cref="AnotarOcAsync"/> al terminar el trabajo.
    /// </summary>
    public async Task<(long? TrabajoId, List<string> Errores)> CrearOcAsync(string ruc, string usuario, string cuenta, EntradaOcLiquidacion entrada, CancellationToken ct = default)
    {
        var (motivo, auditado) = await ExigirRegistroAsync(usuario, ruc, ct);
        if (motivo is not null) return (null, [motivo]);
        var catalogo = await CatalogoAsync(ruc, ct);
        var (payload, errores) = ArmadorOcLiquidacion.Armar(entrada, catalogo.Proveedores.Select(x => x.Id).ToHashSet(StringComparer.Ordinal));
        foreach (var x in entrada.Items.Where(x => !string.IsNullOrWhiteSpace(x.ItemId) && !catalogo.Items.ContainsKey(x.ItemId.Trim())))
        {
            errores.Add($"El ítem {x.ItemId} no existe en Sage como ítem de stock.");
        }
        if (payload is not null && errores.Count == 0)
        {
            await using var cn = await AbrirAsync(ruc, ct);
            if (await LectorImportaciones.ReferenciaUsadaAsync(cn, payload.Referencia, entrada.PostOrder, ct))
            {
                errores.Add($"Ya existe en Sage una OC con la referencia {payload.Referencia}.");
            }
        }
        if (payload is null || errores.Count > 0)
        {
            await AuditarAsync(ruc, usuario, cuenta, entrada.ProveedorId, entrada.Referencia, "RechazoValidacion", "Rechazado", null, null, string.Join(" | ", errores), ct);
            return (null, errores);
        }
        if (!await BridgeHabilitadoAsync(ruc, ct))
        {
            return (null, ["La empresa no está habilitada en el Sage Bridge (administración › Sage Bridge): la OC no se registraría."]);
        }
        if (await TrabajoPendienteAsync(ruc, cuenta, entrada.PostOrder, ct) is { } pendiente)
        {
            return (null, [$"Ya hay un trabajo (#{pendiente.Id}) en el Sage Bridge para esta importación; espera a que termine."]);
        }

        var (id, erroresGuardar) = await GuardarAsync(ruc, usuario, cuenta, entrada.ProveedorId, entrada.Referencia, entrada.Fecha, entrada.Items, entrada.Gastos, ct);
        if (id is null) return (null, erroresGuardar);
        payload.CuentaPorPagar = catalogo.CuentaPorPagar;
        var json = JsonSerializer.Serialize(payload);
        var huella = Huella(json);
        var encolado = await cola.EncolarAsync(ruc, TiposTrabajo.GuardarOcLiquidacion, json, $"liq-oc-{ruc}-{cuenta}-{huella[..16]}-{DateTime.UtcNow:yyyyMMddHHmm}", usuario, ct);
        await AuditarAsync(ruc, auditado, cuenta, entrada.ProveedorId, payload.Referencia, TiposTrabajo.GuardarOcLiquidacion,
            encolado.Nuevo ? "Encolado" : "YaEncolado", encolado.Trabajo.Id, huella, entrada.PostOrder is null ? "OC nueva" : $"Actualiza PostOrder {entrada.PostOrder}", ct);
        return (encolado.Trabajo.Id, []);
    }

    /// <summary>Anota en la liquidación la OC que devolvió el Bridge (el evento <c>Saved</c> del `.exe`).</summary>
    public async Task AnotarOcAsync(string ruc, string cuenta, ResultadoGuardarOcLiquidacion r, DateTime fecha, CancellationToken ct = default)
    {
        await using var db = await peachEbills.CreateDbContextAsync(ct);
        var g = await RepositorioLiquidaciones.UltimaAsync(db, ruc, cuenta, ct);
        if (g is null || r.PostOrder <= 0) return;
        await RepositorioLiquidaciones.AnotarOcAsync(db, g.Id, r.PostOrder, r.Clave, r.Referencia, fecha, ct);
    }

    /// <summary>«Registrar compra» (D1): encola <see cref="TiposTrabajo.ConvertirLiquidacion"/> de la OC.</summary>
    public async Task<(long? TrabajoId, string? Error)> RegistrarCompraAsync(string ruc, string usuario, string cuenta, OcLiquidacion oc, CancellationToken ct = default)
    {
        var (motivo, auditado) = await ExigirRegistroAsync(usuario, ruc, ct);
        if (motivo is not null) return (null, motivo);
        if (!await BridgeHabilitadoAsync(ruc, ct))
        {
            return (null, "La empresa no está habilitada en el Sage Bridge (administración › Sage Bridge): la compra no se registraría.");
        }
        if (await TrabajoPendienteAsync(ruc, cuenta, oc.PostOrder, ct) is { } pendiente)
        {
            return (null, $"Ya hay un trabajo (#{pendiente.Id}) en el Sage Bridge para esta importación; espera a que termine.");
        }
        var json = JsonSerializer.Serialize(new PayloadConvertirLiquidacion
        {
            PostOrder = oc.PostOrder, ReferenciaCompra = (await CatalogoAsync(ruc, ct)).ReferenciaCompra(oc.Referencia),
        });
        var encolado = await cola.EncolarAsync(ruc, TiposTrabajo.ConvertirLiquidacion, json, $"liq-compra-{ruc}-{oc.PostOrder}-{DateTime.UtcNow:yyyyMMddHHmm}", usuario, ct);
        await AuditarAsync(ruc, auditado, cuenta, oc.ProveedorId, oc.Referencia, TiposTrabajo.ConvertirLiquidacion,
            encolado.Nuevo ? "Encolado" : "YaEncolado", encolado.Trabajo.Id, Huella(json), $"OC {oc.PostOrder}", ct);
        return (encolado.Trabajo.Id, null);
    }

    public Task<TrabajoSage?> TrabajoAsync(long id, CancellationToken ct = default) => cola.ObtenerAsync(id, ct);

    /// <summary>
    /// Trabajo de OC o de compra de esta importación que todavía no terminó (en cola o en proceso): la página lo retoma al recargar y no
    /// deja encolar otro mientras tanto.
    /// </summary>
    public async Task<TrabajoSage?> TrabajoPendienteAsync(string ruc, string cuenta, int? postOrder, CancellationToken ct = default)
    {
        var oc = $"\"CuentaImportacion\":{JsonSerializer.Serialize(cuenta)}";
        var compra = postOrder is { } po ? $"\"PostOrder\":{po}}}" : null;
        foreach (var tipo in new[] { TiposTrabajo.GuardarOcLiquidacion, TiposTrabajo.ConvertirLiquidacion })
        {
            var t = (await cola.ListarAsync(new FiltroTrabajos(ruc, null, tipo, 200), ct))
                .Where(x => !EstadosTrabajo.EsFinal(x.Estado) && x.PayloadJson is { } p
                            && (tipo == TiposTrabajo.GuardarOcLiquidacion ? p.Contains(oc, StringComparison.Ordinal) : compra is not null && p.Contains(compra, StringComparison.Ordinal)))
                .OrderByDescending(x => x.Id).FirstOrDefault();
            if (t is not null) return t;
        }
        return null;
    }

    /// <summary>¿Hay algún Sage Bridge vivo (latido reciente)? Sin él los trabajos se quedan en cola.</summary>
    public async Task<bool> BridgeVivoAsync(CancellationToken ct = default) =>
        (await cola.LatidosAsync(ct)).Any(l => EstadoBridge.EstaVivo(l, DateTime.UtcNow));

    public async Task<bool> BridgeHabilitadoAsync(string ruc, CancellationToken ct = default) =>
        (await cola.EmpresasAsync(ct)).Any(e => e.Ruc == ruc && e.Habilitada);

    /// <summary>Nombre de la empresa para el reporte (C1: el `.exe` ponía «CPTDC ECUADOR S.A.» fijo).</summary>
    public async Task<string> NombreEmpresaAsync(string ruc, CancellationToken ct = default)
    {
        await using var db = await peachEbills.CreateDbContextAsync(ct);
        var t = await db.Transmitter.AsNoTracking().FirstOrDefaultAsync(x => x.Ruc == ruc, ct);
        return (t?.NameAlias ?? t?.Name ?? ruc).Trim();
    }

    private async Task<List<string>> ValidarCabeceraAsync(string ruc, string proveedorId, string referencia, CancellationToken ct)
    {
        var errores = new List<string>();
        var catalogo = await CatalogoAsync(ruc, ct);
        if (string.IsNullOrWhiteSpace(proveedorId) || !catalogo.Proveedores.Any(x => x.Id == proveedorId.Trim()))
        {
            errores.Add("Debe indicar un proveedor válido.");
        }
        if (string.IsNullOrWhiteSpace(referencia)) errores.Add("Debe indicar un número de referencia para guardar o subir a SAGE.");
        return errores;
    }

    private Task AuditarAsync(string ruc, string usuario, string cuenta, string proveedor, string referencia, string accion, string resultado,
        long? trabajo, string? huella, string? detalle, CancellationToken ct) =>
        auditoria.RegistrarAsync(new AuditoriaRegistroSage
        {
            Ruc = ruc,
            Modulo = Modulo,
            Accion = accion,
            Usuario = usuario,
            Documento = referencia,
            Tercero = proveedor,
            Referencia = cuenta,
            TrabajoId = trabajo,
            Resultado = resultado,
            Detalle = detalle,
            HuellaPayload = huella,
        }, ct);

    private static string Huella(string texto) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));

    private async Task<OdbcConnection> AbrirAsync(string ruc, CancellationToken ct)
    {
        var cadena = await resolver.CadenaOdbcAsync(ruc, ct);
        var cn = cadena is null ? conexiones.CreateConnection() : conexiones.CreateConnection(cadena);
        await cn.OpenAsync(ct);
        return cn;
    }
}
