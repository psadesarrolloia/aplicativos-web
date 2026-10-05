using Microsoft.Extensions.Logging;
using PsaWeb.Notificaciones;
using PsaWeb.Ventas.Prefacturas;

namespace PsaWeb.Modules.Ventas.Prefacturas;

/// <summary>Resultado de emitir: la prefactura guardada (o <c>null</c> si la validación la rechazó) y las advertencias/errores.</summary>
public sealed record ResultadoEmision(Prefactura? Prefactura, ResultadoValidacion Validacion);

/// <summary>
/// Emite la prefactura (valida, calcula, numera y guarda), genera su PDF y avisa por correo a Contabilidad (1–2 destinatarios por empresa) con todos los
/// datos para digitarla a mano en Sage. El correo <b>nunca</b> hace fallar la emisión: si no hay SMTP o falla, la prefactura queda guardada con el estado
/// del correo y se puede reenviar.
/// <para><b>Permisos</b> (<see cref="ReglasVentas"/>), exigidos aquí y no solo en las pantallas: emitir = <c>mkSalesQte</c>; ver = <c>quSalesQte</c> (un usuario
/// sin <c>auSalesQte</c> solo ve las suyas); reenviar el correo = emisor de esa prefactura o Contabilidad; marcar facturada y anular = <c>auSalesQte</c>.</para>
/// </summary>
public sealed class ServicioPrefacturas(IAlmacenPrefacturas almacen, IServicioCorreo correo, TimeProvider reloj, ILogger<ServicioPrefacturas> logger)
{
    private static readonly TimeSpan EsperaMaximaCorreo = TimeSpan.FromSeconds(40);

    public DateOnly Hoy => DateOnly.FromDateTime(reloj.GetLocalNow().DateTime);

    public Task<ConfiguracionVentas> ConfiguracionAsync(string ruc, CancellationToken ct = default) => almacen.ConfiguracionAsync(ruc, ct);

    /// <summary>La configuración la gestionan los administradores (la pantalla ya exige <c>Plataforma:Admins</c>).</summary>
    public Task GuardarConfiguracionAsync(ConfiguracionVentas config, string? usuario, CancellationToken ct = default) =>
        almacen.GuardarConfiguracionAsync(config, usuario, ct);

    /// <summary>La prefactura, o <c>null</c> si no existe o el actor no puede verla (se oculta su existencia).</summary>
    public async Task<Prefactura?> ObtenerAsync(string ruc, int id, ActorVentas actor, CancellationToken ct = default)
    {
        if (!actor.Permisos.VerPrefacturas) return null;
        var p = await almacen.ObtenerAsync(ruc, id, ct);
        return p is not null && PuedeVer(p, actor) ? p : null;
    }

    public async Task<IReadOnlyList<Prefactura>> ListarAsync(string ruc, FiltroPrefacturas filtro, ActorVentas actor, CancellationToken ct = default)
    {
        if (!actor.Permisos.VerPrefacturas) throw new AccesoDenegadoVentasException("No tenés permiso para ver prefacturas.");
        // Sin «cerrar» solo se ven las propias, sin importar el filtro que mande la pantalla.
        var efectivo = actor.Permisos.VerTodas ? filtro : filtro with { CreadaPor = actor.Usuario };
        return await almacen.ListarAsync(ruc, efectivo, ct);
    }

    public async Task<ResultadoEmision> EmitirAsync(string ruc, string empresaNombre, SolicitudPrefactura solicitud, ActorVentas actor, string? urlBase,
        CancellationToken ct = default)
    {
        if (!actor.Permisos.Emitir) throw new AccesoDenegadoVentasException("No tenés permiso para emitir prefacturas.");
        var config = await almacen.ConfiguracionAsync(ruc, ct);
        var validacion = ValidadorPrefactura.Validar(solicitud, config);
        if (!validacion.EsValida) return new ResultadoEmision(null, validacion);

        var lineas = CalculadoraPrefactura.Calcular(solicitud.Lineas);
        var (subtotal, iva, total) = CalculadoraPrefactura.Totales(lineas, solicitud.AplicaIva, config.PorcentajeIva);
        var hoy = Hoy;
        var nueva = new Prefactura(
            Id: 0, ruc, empresaNombre.Trim(), Numero: 0, hoy, hoy.AddDays(Math.Max(config.VigenciaDias, 1)), EstadoPrefactura.Emitida,
            solicitud.ClienteId.Trim(), solicitud.ClienteNombre.Trim(), solicitud.ClienteContacto.Trim(), solicitud.ClienteTelefono.Trim(), solicitud.ClienteEmail.Trim(),
            solicitud.ListaDePrecios, solicitud.DiasCredito, CalculadoraPrefactura.TerminosSage(solicitud.DiasCredito),
            solicitud.Vendedor.Trim(), solicitud.Etiqueta.Trim(), solicitud.OrdenCliente.Trim(), solicitud.DireccionEnvio.Trim(),
            solicitud.NotaCliente.Trim(), solicitud.NotaInterna.Trim(),
            solicitud.AplicaIva ? config.CodigoImpuesto : string.Empty, solicitud.AplicaIva ? config.PorcentajeIva : 0m,
            subtotal, iva, total, lineas, actor.Usuario, reloj.GetUtcNow().UtcDateTime,
            EstadoCorreo.Pendiente, string.Empty, null, null, null, null, null);

        var guardada = await almacen.GuardarAsync(nueva, ct);
        var conCorreo = await EnviarCorreoAsync(guardada, config, urlBase, ct);
        return new ResultadoEmision(conCorreo, validacion);
    }

    /// <summary>Reenvía el correo a Contabilidad (por ejemplo tras un fallo del SMTP o si cambiaron los destinatarios): quien la emitió o Contabilidad.</summary>
    public async Task<Prefactura> ReenviarCorreoAsync(string ruc, int id, string? urlBase, ActorVentas actor, CancellationToken ct = default)
    {
        var p = await ObtenerAsync(ruc, id, actor, ct) ?? throw new InvalidOperationException("La prefactura no existe.");
        var propia = string.Equals(p.CreadaPor, actor.Usuario, StringComparison.OrdinalIgnoreCase);
        if (!actor.Permisos.Cerrar && !(actor.Permisos.Emitir && propia)) throw new AccesoDenegadoVentasException("No tenés permiso para reenviar el correo de esta prefactura.");
        return await EnviarCorreoAsync(p, await almacen.ConfiguracionAsync(ruc, ct), urlBase, ct);
    }

    public async Task MarcarFacturadaAsync(string ruc, int id, string facturaSage, ActorVentas actor, CancellationToken ct = default)
    {
        if (!actor.Permisos.Cerrar) throw new AccesoDenegadoVentasException("Solo Contabilidad puede marcar una prefactura como facturada.");
        if (string.IsNullOrWhiteSpace(facturaSage)) throw new ArgumentException("Anotá el número de la factura de Sage.", nameof(facturaSage));
        var p = await ObtenerAsync(ruc, id, actor, ct) ?? throw new InvalidOperationException("La prefactura no existe.");
        if (p.Estado != EstadoPrefactura.Emitida) throw new InvalidOperationException($"La prefactura {p.NumeroTexto} ya está {p.Estado.ToString().ToLowerInvariant()}.");
        await almacen.MarcarFacturadaAsync(ruc, id, facturaSage, actor.Usuario, reloj.GetUtcNow().UtcDateTime, ct);
    }

    public async Task AnularAsync(string ruc, int id, ActorVentas actor, CancellationToken ct = default)
    {
        if (!actor.Permisos.Cerrar) throw new AccesoDenegadoVentasException("Solo Contabilidad puede anular una prefactura.");
        var p = await ObtenerAsync(ruc, id, actor, ct) ?? throw new InvalidOperationException("La prefactura no existe.");
        if (p.Estado != EstadoPrefactura.Emitida) throw new InvalidOperationException($"La prefactura {p.NumeroTexto} ya está {p.Estado.ToString().ToLowerInvariant()}.");
        await almacen.CambiarEstadoAsync(ruc, id, EstadoPrefactura.Anulada, ct);
    }

    private static bool PuedeVer(Prefactura p, ActorVentas actor) =>
        actor.Permisos.VerTodas || string.Equals(p.CreadaPor, actor.Usuario, StringComparison.OrdinalIgnoreCase);

    private async Task<Prefactura> EnviarCorreoAsync(Prefactura p, ConfiguracionVentas config, string? urlBase, CancellationToken ct)
    {
        var destinatarios = config.Destinatarios();
        var lista = string.Join(", ", destinatarios);
        if (destinatarios.Count == 0)
        {
            await almacen.RegistrarCorreoAsync(p.Ruc, p.Id, EstadoCorreo.NoConfigurado, string.Empty,
                "La empresa no tiene correos de Contabilidad configurados (Configuración > Ventas).", null, ct);
        }
        else if (!correo.Disponible)
        {
            await almacen.RegistrarCorreoAsync(p.Ruc, p.Id, EstadoCorreo.NoConfigurado, lista, "El servidor de correo (SMTP) no está configurado.", null, ct);
        }
        else
        {
            try
            {
                using var espera = CancellationTokenSource.CreateLinkedTokenSource(ct);
                espera.CancelAfter(EsperaMaximaCorreo);
                var pdf = PrefacturaPdf.Generar(p);
                var url = string.IsNullOrWhiteSpace(urlBase) ? null : urlBase!.TrimEnd('/') + $"/ventas/prefacturas/{p.Id}";
                await correo.EnviarAsync(new MensajeCorreo(destinatarios, PrefacturaCorreo.Asunto(p), PrefacturaCorreo.CuerpoHtml(p, url),
                    new[] { new AdjuntoCorreo(PrefacturaPdf.NombreDeArchivo(p), pdf, "application/pdf") }), espera.Token);
                await almacen.RegistrarCorreoAsync(p.Ruc, p.Id, EstadoCorreo.Enviado, lista, null, reloj.GetUtcNow().UtcDateTime, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "No se pudo enviar el correo de la prefactura {Numero} de {Ruc}.", p.NumeroTexto, p.Ruc);
                await almacen.RegistrarCorreoAsync(p.Ruc, p.Id, EstadoCorreo.Fallo, lista, ex.Message, null, ct);
            }
        }

        return await almacen.ObtenerAsync(p.Ruc, p.Id, ct) ?? p;
    }
}
