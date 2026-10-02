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
/// </summary>
public sealed class ServicioPrefacturas(IAlmacenPrefacturas almacen, IServicioCorreo correo, TimeProvider reloj, ILogger<ServicioPrefacturas> logger)
{
    private static readonly TimeSpan EsperaMaximaCorreo = TimeSpan.FromSeconds(40);

    public DateOnly Hoy => DateOnly.FromDateTime(reloj.GetLocalNow().DateTime);

    public Task<ConfiguracionVentas> ConfiguracionAsync(string ruc, CancellationToken ct = default) => almacen.ConfiguracionAsync(ruc, ct);

    public Task GuardarConfiguracionAsync(ConfiguracionVentas config, string? usuario, CancellationToken ct = default) =>
        almacen.GuardarConfiguracionAsync(config, usuario, ct);

    public Task<Prefactura?> ObtenerAsync(string ruc, int id, CancellationToken ct = default) => almacen.ObtenerAsync(ruc, id, ct);

    public Task<IReadOnlyList<Prefactura>> ListarAsync(string ruc, FiltroPrefacturas filtro, CancellationToken ct = default) => almacen.ListarAsync(ruc, filtro, ct);

    public async Task<ResultadoEmision> EmitirAsync(string ruc, string empresaNombre, SolicitudPrefactura solicitud, string usuario, string? urlBase,
        CancellationToken ct = default)
    {
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
            subtotal, iva, total, lineas, usuario, reloj.GetUtcNow().UtcDateTime,
            EstadoCorreo.Pendiente, string.Empty, null, null, null, null, null);

        var guardada = await almacen.GuardarAsync(nueva, ct);
        var conCorreo = await EnviarCorreoAsync(guardada, config, urlBase, ct);
        return new ResultadoEmision(conCorreo, validacion);
    }

    /// <summary>Reenvía el correo a Contabilidad (por ejemplo tras un fallo del SMTP o si cambiaron los destinatarios).</summary>
    public async Task<Prefactura> ReenviarCorreoAsync(string ruc, int id, string? urlBase, CancellationToken ct = default)
    {
        var p = await almacen.ObtenerAsync(ruc, id, ct) ?? throw new InvalidOperationException("La prefactura no existe.");
        return await EnviarCorreoAsync(p, await almacen.ConfiguracionAsync(ruc, ct), urlBase, ct);
    }

    public async Task MarcarFacturadaAsync(string ruc, int id, string facturaSage, string usuario, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(facturaSage)) throw new ArgumentException("Anotá el número de la factura de Sage.", nameof(facturaSage));
        var p = await almacen.ObtenerAsync(ruc, id, ct) ?? throw new InvalidOperationException("La prefactura no existe.");
        if (p.Estado != EstadoPrefactura.Emitida) throw new InvalidOperationException($"La prefactura {p.NumeroTexto} ya está {p.Estado.ToString().ToLowerInvariant()}.");
        await almacen.MarcarFacturadaAsync(ruc, id, facturaSage, usuario, reloj.GetUtcNow().UtcDateTime, ct);
    }

    public async Task AnularAsync(string ruc, int id, CancellationToken ct = default)
    {
        var p = await almacen.ObtenerAsync(ruc, id, ct) ?? throw new InvalidOperationException("La prefactura no existe.");
        if (p.Estado != EstadoPrefactura.Emitida) throw new InvalidOperationException($"La prefactura {p.NumeroTexto} ya está {p.Estado.ToString().ToLowerInvariant()}.");
        await almacen.CambiarEstadoAsync(ruc, id, EstadoPrefactura.Anulada, ct);
    }

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
