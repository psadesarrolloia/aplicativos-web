using System.Globalization;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PsaWeb.Recibidos.Data;

namespace PsaWeb.Recibidos;

public sealed record ResumenDescargaXml(int Pedidas, int Descargadas, int YaEstaban, int FueraDeVentana, int Errores, int Rechazadas);

/// <summary>
/// Trae del WS del SRI el XML de claves que todavía no lo tienen y lo guarda en el almacén (§4.4). Lo que el WS no entrega
/// queda en <c>DescargasXmlFallidas</c> (para la subida manual y para medir, §13.5).
/// </summary>
public sealed class ServicioDescargaXml(
    IDescargadorXmlSri descargador,
    IAlmacenXmlRecibidos almacen,
    IDbContextFactory<RecibidosDbContext> contextos,
    TimeProvider? reloj = null)
{
    /// <summary>Pausa entre llamadas al WS (el `.exe`/scripts de la F2 usaban 150 ms sin problemas).</summary>
    public static TimeSpan Pausa { get; set; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Pasada la ventana del WS no se vuelve a intentar una clave que ya respondió «sin comprobante».</summary>
    public const int DiasVentanaWs = 20;

    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    public async Task<ResumenDescargaXml> DescargarAsync(string ruc, IEnumerable<string> claves, string usuario,
        bool aceptarOtroReceptor = false, IProgress<int>? avance = null, CancellationToken cancellationToken = default)
    {
        var lista = claves.Where(EsClave).Distinct().ToList();
        var conXml = await almacen.ConXmlAsync(ruc, lista, cancellationToken);
        Dictionary<string, DescargaXmlFallida> fallidas;
        await using (var db = await contextos.CreateDbContextAsync(cancellationToken))
        {
            fallidas = (await db.DescargasFallidas.AsNoTracking().Where(x => x.Ruc == ruc).ToListAsync(cancellationToken))
                .ToDictionary(x => x.ClaveAcceso);
        }

        int descargadas = 0, fuera = 0, errores = 0, rechazadas = 0, hechas = 0;
        var hoy = DateOnly.FromDateTime(_reloj.GetLocalNow().DateTime);
        foreach (var clave in lista)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (conXml.Contains(clave)) { avance?.Report(++hechas); continue; }
            if (fallidas.TryGetValue(clave, out var f) && f.Motivo == MotivoSinXml
                && FechaEmision(clave) is { } fecha && hoy.DayNumber - fecha.DayNumber > DiasVentanaWs
                && f.UltimoIntentoUtc.Date > fecha.ToDateTime(TimeOnly.MinValue).AddDays(DiasVentanaWs))
            {
                fuera++;
                avance?.Report(++hechas);
                continue;
            }

            var r = await descargador.DescargarAsync(clave, cancellationToken);
            if (r.Contenido is not null)
            {
                var g = await almacen.GuardarAsync(ruc, r.Contenido, OrigenesXml.WsSri, usuario, clave, aceptarOtroReceptor, cancellationToken);
                if (g.Resultado is ResultadoGuardadoXml.Guardado or ResultadoGuardadoXml.YaExistia) descargadas++;
                else { rechazadas++; await RegistrarFallaAsync(ruc, clave, MotivoRechazado, g.Motivo, cancellationToken); }
            }
            else if (r.SinComprobante)
            {
                fuera++;
                await RegistrarFallaAsync(ruc, clave, MotivoSinXml, null, cancellationToken);
            }
            else
            {
                errores++;
                await RegistrarFallaAsync(ruc, clave, MotivoError, r.Error, cancellationToken);
            }
            avance?.Report(++hechas);
            if (Pausa > TimeSpan.Zero) await Task.Delay(Pausa, cancellationToken);
        }
        return new ResumenDescargaXml(lista.Count, descargadas, conXml.Count, fuera, errores, rechazadas);
    }

    public const string MotivoSinXml = "SinXml";
    public const string MotivoError = "Error";
    public const string MotivoRechazado = "Rechazado";

    /// <summary>Claves que el WS no entregó (para la bandeja: «subir a mano»).</summary>
    public async Task<IReadOnlyDictionary<string, DescargaXmlFallida>> FallidasAsync(string ruc, CancellationToken cancellationToken = default)
    {
        await using var db = await contextos.CreateDbContextAsync(cancellationToken);
        return (await db.DescargasFallidas.AsNoTracking().Where(x => x.Ruc == ruc).ToListAsync(cancellationToken))
            .ToDictionary(x => x.ClaveAcceso);
    }

    private async Task RegistrarFallaAsync(string ruc, string clave, string motivo, string? detalle, CancellationToken ct)
    {
        await using var db = await contextos.CreateDbContextAsync(ct);
        var f = await db.DescargasFallidas.FirstOrDefaultAsync(x => x.Ruc == ruc && x.ClaveAcceso == clave, ct);
        if (f is null)
        {
            f = new DescargaXmlFallida { Ruc = ruc, ClaveAcceso = clave };
            db.DescargasFallidas.Add(f);
        }
        f.Motivo = motivo;
        f.Detalle = detalle is { Length: > 500 } ? detalle[..500] : detalle;
        f.Intentos++;
        f.UltimoIntentoUtc = _reloj.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    public static bool EsClave(string? clave) => clave is { Length: 49 } && clave.All(char.IsDigit);

    /// <summary>Los primeros 8 dígitos de la clave de acceso son la fecha de emisión (ddMMaaaa).</summary>
    public static DateOnly? FechaEmision(string clave) =>
        EsClave(clave) && DateOnly.TryParseExact(clave[..8], "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;

    /// <summary>Tipo por la clave (dígitos 9-10: 01 factura, 04 nota de crédito, 07 retención).</summary>
    public static string? CodigoTipo(string clave) => EsClave(clave) ? clave.Substring(8, 2) : null;
}

/// <summary>Pedido de descarga en segundo plano (al llegar el reporte de la extensión).</summary>
public sealed record PedidoDescargaXml(string Ruc, IReadOnlyList<string> Claves, string Usuario);

/// <summary>Cola en memoria de descargas: el endpoint del reporte responde enseguida y el XML se baja detrás.</summary>
public sealed class ColaDescargaXml
{
    private readonly Channel<PedidoDescargaXml> _canal = Channel.CreateUnbounded<PedidoDescargaXml>();

    public bool Encolar(PedidoDescargaXml pedido) => pedido.Claves.Count > 0 && _canal.Writer.TryWrite(pedido);

    internal ChannelReader<PedidoDescargaXml> Lector => _canal.Reader;
}

public sealed class TrabajadorDescargaXml(ColaDescargaXml cola, IServiceScopeFactory scopes, ILogger<TrabajadorDescargaXml> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var pedido in cola.Lector.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var servicio = scope.ServiceProvider.GetRequiredService<ServicioDescargaXml>();
                var r = await servicio.DescargarAsync(pedido.Ruc, pedido.Claves, pedido.Usuario, cancellationToken: stoppingToken);
                log.LogInformation("XML recibidos {Ruc}: {Descargadas} descargados, {YaEstaban} ya estaban, {Fuera} fuera de la ventana del WS, {Errores} errores, {Rechazadas} rechazados.",
                    pedido.Ruc, r.Descargadas, r.YaEstaban, r.FueraDeVentana, r.Errores, r.Rechazadas);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Descarga de XML recibidos de {Ruc}", pedido.Ruc);
            }
        }
    }
}
