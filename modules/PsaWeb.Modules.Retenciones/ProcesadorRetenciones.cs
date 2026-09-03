using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PsaWeb.Comprobantes.Retenciones;
using PsaWeb.Datil;
using PsaWeb.Modules.Retenciones.Data;
using PsaWeb.PeachEbills;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.Retenciones;

public sealed record ResumenEmpresa(
    string Ruc, string Nombre, int Pendientes, int Emitidas, int ConErrores, IReadOnlyList<string> Mensajes);

public sealed record ResumenCorrida(
    DateTimeOffset Inicio, DateTimeOffset Fin, bool DryRun, IReadOnlyList<ResumenEmpresa> Empresas)
{
    public int TotalEmitidas => Empresas.Sum(e => e.Emitidas);
    public int TotalConErrores => Empresas.Sum(e => e.ConErrores);
    public int TotalPendientes => Empresas.Sum(e => e.Pendientes);
}

/// <summary>
/// Orquesta la generación de retenciones: por cada empresa activa busca las
/// facturas de compra pendientes, arma la retención, la emite en Datil (o no, en
/// modo <c>DryRun</c>) y persiste el resultado. Port del bucle de
/// <c>AutomaticTwhSender/Program.cs</c>.
/// </summary>
public sealed class ProcesadorRetenciones
{
    private readonly PendientesRepository _pendientes;
    private readonly EmpresaLookup _empresas;
    private readonly PeachConnStringResolver _conexionesSage;
    private readonly ISageConnectionFactory _sageFactory;
    private readonly RetencionBuilder _builder;
    private readonly IDatilClient _datil;
    private readonly RepositorioRetenciones _repositorio;
    private readonly RetencionesOptions _opciones;
    private readonly bool _dryRun;
    private readonly ILogger<ProcesadorRetenciones> _logger;

    public ProcesadorRetenciones(
        PendientesRepository pendientes,
        EmpresaLookup empresas,
        PeachConnStringResolver conexionesSage,
        ISageConnectionFactory sageFactory,
        RetencionBuilder builder,
        IDatilClient datil,
        RepositorioRetenciones repositorio,
        IOptions<RetencionesOptions> opciones,
        IOptions<DatilOptions> datilOpciones,
        ILogger<ProcesadorRetenciones> logger)
    {
        _pendientes = pendientes;
        _empresas = empresas;
        _conexionesSage = conexionesSage;
        _sageFactory = sageFactory;
        _builder = builder;
        _datil = datil;
        _repositorio = repositorio;
        _opciones = opciones.Value;
        _dryRun = datilOpciones.Value.DryRun;
        _logger = logger;
    }

    public async Task<ResumenCorrida> ProcesarTodasAsync(string usuario, CancellationToken cancellationToken = default)
    {
        var inicio = DateTimeOffset.Now;
        var empresas = await _pendientes.EmpresasActivasAsync(_opciones.OmitirRucs, _opciones.AmbienteForzado, cancellationToken);

        var resumenes = new List<ResumenEmpresa>();
        foreach (var empresa in empresas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            resumenes.Add(await ProcesarEmpresaAsync(empresa, usuario, cancellationToken));
        }

        return new ResumenCorrida(inicio, DateTimeOffset.Now, _dryRun, resumenes);
    }

    public async Task<ResumenEmpresa> ProcesarEmpresaAsync(
        EmpresaActiva empresa, string usuario, CancellationToken cancellationToken = default)
    {
        var mensajes = new List<string>();

        IReadOnlyList<string> pendientes;
        try
        {
            pendientes = await _pendientes.PendientesAsync(empresa.Ruc, empresa.Ambiente, cancellationToken);
        }
        catch (Exception ex)
        {
            return Fallo(empresa, 0, $"No se pudieron leer los pendientes: {ex.Message}");
        }

        if (pendientes.Count == 0)
        {
            return new ResumenEmpresa(empresa.Ruc, empresa.Nombre, 0, 0, 0, mensajes);
        }

        EmpresaEmisora emisor;
        DatilEmpresa datilEmpresa;
        string cadenaSage;
        try
        {
            emisor = await _empresas.EmisorAsync(empresa.Ruc, cancellationToken);
            datilEmpresa = await _empresas.DatilAsync(empresa.Ruc, cancellationToken);
            cadenaSage = await _conexionesSage.ResolverCadenaOdbcAsync(empresa.Ruc, cancellationToken);
        }
        catch (Exception ex)
        {
            return Fallo(empresa, pendientes.Count, $"Configuración incompleta: {ex.Message}");
        }

        await using var conexion = _sageFactory.CreateConnection(cadenaSage);
        try
        {
            await conexion.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return Fallo(empresa, pendientes.Count, $"No se pudo abrir Sage 50: {ex.Message}");
        }

        var emitidas = 0;
        var conErrores = 0;

        foreach (var piPostOrder in pendientes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var leida = await LectorCompraRetencion.LeerAsync(conexion, piPostOrder, cancellationToken);
                if (leida is null)
                {
                    conErrores++;
                    mensajes.Add($"[{piPostOrder}] no se encontró la compra en Sage 50.");
                    continue;
                }

                var resultado = await _builder.ArmarAsync(
                    emisor, leida.Compra, leida.Proveedor, empresa.Ambiente, datilEmpresa.EmailPruebas, cancellationToken);

                if (!resultado.Ok)
                {
                    conErrores++;
                    mensajes.Add($"[{piPostOrder}] {string.Join(" | ", resultado.Errores)}");
                    continue;
                }

                var datil = await _datil.EmitirRetencionAsync(resultado.Retencion!, datilEmpresa.Credenciales, cancellationToken);

                if (datil.FueDryRun)
                {
                    emitidas++;
                    mensajes.Add($"[{piPostOrder}] dry-run OK — retención {resultado.NumeroRetencion} (no se envió ni se guardó).");
                    continue;
                }

                if (!datil.Emitido)
                {
                    conErrores++;
                    mensajes.Add($"[{piPostOrder}] Datil rechazó: {string.Join(" | ", datil.Errores)}");
                    continue;
                }

                var thid = await _repositorio.GuardarAsync(
                    empresa.Ruc, resultado, leida.Proveedor, empresa.Ambiente, datil, usuario, cancellationToken);

                emitidas++;
                mensajes.Add($"[{piPostOrder}] retención {resultado.NumeroRetencion} emitida (Datil id {datil.Id}), THId {thid}.");
            }
            catch (Exception ex)
            {
                conErrores++;
                mensajes.Add($"[{piPostOrder}] error: {ex.Message}");
                _logger.LogError(ex, "Retención {PiPostOrder} de {Ruc}", piPostOrder, empresa.Ruc);
            }
        }

        return new ResumenEmpresa(empresa.Ruc, empresa.Nombre, pendientes.Count, emitidas, conErrores, mensajes);
    }

    private static ResumenEmpresa Fallo(EmpresaActiva empresa, int pendientes, string mensaje) =>
        new(empresa.Ruc, empresa.Nombre, pendientes, 0, 1, new[] { mensaje });
}
