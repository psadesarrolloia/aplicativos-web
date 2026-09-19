using System.Data.Odbc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PsaWeb.Comprobantes.Retenciones;
using PsaWeb.Datil;
using PsaWeb.Modules.ComprobantesElectronicos.Retenciones.Data;
using PsaWeb.PeachEbills;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.ComprobantesElectronicos.Retenciones;

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

    /// <summary>Procesa una sola empresa (la de la sesión). Falla con gracia si no está activa.</summary>
    public async Task<ResumenCorrida> ProcesarUnaAsync(
        string ruc, string usuario, CancellationToken cancellationToken = default)
    {
        var inicio = DateTimeOffset.Now;
        var empresas = await _pendientes.EmpresasActivasAsync(
            _opciones.OmitirRucs, _opciones.AmbienteForzado, cancellationToken);

        var empresa = empresas.FirstOrDefault(e => e.Ruc == ruc);
        if (empresa is null)
        {
            var fallo = new ResumenEmpresa(ruc, ruc, 0, 0, 1,
                new[] { "La empresa no está activa o está en la lista de omitidas." });
            return new ResumenCorrida(inicio, DateTimeOffset.Now, _dryRun, new[] { fallo });
        }

        var resumen = await ProcesarEmpresaAsync(empresa, usuario, cancellationToken);
        return new ResumenCorrida(inicio, DateTimeOffset.Now, _dryRun, new[] { resumen });
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

        ContextoEmpresa contexto;
        try
        {
            contexto = await CargarContextoAsync(empresa.Ruc, cancellationToken);
        }
        catch (Exception ex)
        {
            return Fallo(empresa, pendientes.Count, $"Configuración incompleta: {ex.Message}");
        }

        await using var conexion = _sageFactory.CreateConnection(contexto.CadenaSage);
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
            var r = await ProcesarCompraAsync(
                conexion, contexto, empresa.Ruc, empresa.Ambiente, usuario, piPostOrder, cancellationToken);
            if (r.Ok) emitidas++; else conErrores++;
            mensajes.AddRange(r.Mensajes);
        }

        return new ResumenEmpresa(empresa.Ruc, empresa.Nombre, pendientes.Count, emitidas, conErrores, mensajes);
    }

    // ===================== Uso desde la interfaz unificada (por fila / por rango) =====================

    /// <summary>
    /// Compras de Sage 50 pendientes de retención en un rango de fechas: los pendientes
    /// de <c>PurchaseOrderSync</c> (SQL, sin fecha) cruzados con la fecha y el número
    /// de la compra en Sage.
    /// </summary>
    public async Task<IReadOnlyList<CompraPendiente>> ListarPendientesAsync(
        string ruc, short ambiente, DateTime desde, DateTime hasta, CancellationToken cancellationToken = default)
    {
        var pendientes = await _pendientes.PendientesAsync(ruc, AmbienteEfectivo(ambiente), cancellationToken);
        if (pendientes.Count == 0) return Array.Empty<CompraPendiente>();

        var cadenaSage = await _conexionesSage.ResolverCadenaOdbcAsync(ruc, cancellationToken);
        await using var conexion = _sageFactory.CreateConnection(cadenaSage);
        await conexion.OpenAsync(cancellationToken);

        var enRango = await LectorComprasPendientes.ListarAsync(conexion, desde, hasta, cancellationToken);
        var set = pendientes.ToHashSet(StringComparer.Ordinal);
        return enRango.Where(c => set.Contains(c.PostOrder)).ToList();
    }

    /// <summary>Genera la retención de UNA compra (botón «Generar» de una fila).</summary>
    public async Task<ResultadoComprobante> ProcesarUnaCompraAsync(
        string ruc, short ambiente, string usuario, string piPostOrder, CancellationToken cancellationToken = default)
    {
        try
        {
            var contexto = await CargarContextoAsync(ruc, cancellationToken);
            await using var conexion = _sageFactory.CreateConnection(contexto.CadenaSage);
            await conexion.OpenAsync(cancellationToken);
            return await ProcesarCompraAsync(
                conexion, contexto, ruc, AmbienteEfectivo(ambiente), usuario, piPostOrder, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Retención {PiPostOrder} de {Ruc}", piPostOrder, ruc);
            return ResultadoComprobante.Error(piPostOrder, string.Empty, ex.Message);
        }
    }

    /// <summary>Genera las retenciones pendientes de una empresa en un rango de fechas («Procesar lote»).</summary>
    public async Task<ResumenLote> ProcesarLoteAsync(
        string ruc, short ambiente, DateTime desde, DateTime hasta, string usuario,
        CancellationToken cancellationToken = default)
    {
        var inicio = DateTimeOffset.Now;
        var resultados = new List<ResultadoComprobante>();
        var ambienteEf = AmbienteEfectivo(ambiente);
        try
        {
            var contexto = await CargarContextoAsync(ruc, cancellationToken);
            await using var conexion = _sageFactory.CreateConnection(contexto.CadenaSage);
            await conexion.OpenAsync(cancellationToken);

            var pendientes = await _pendientes.PendientesAsync(ruc, ambienteEf, cancellationToken);
            var set = pendientes.ToHashSet(StringComparer.Ordinal);
            var enRango = (await LectorComprasPendientes.ListarAsync(conexion, desde, hasta, cancellationToken))
                .Where(c => set.Contains(c.PostOrder))
                .ToList();

            foreach (var compra in enRango)
            {
                cancellationToken.ThrowIfCancellationRequested();
                resultados.Add(await ProcesarCompraAsync(
                    conexion, contexto, ruc, ambienteEf, usuario, compra.PostOrder, cancellationToken));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Lote de retenciones de {Ruc}", ruc);
            resultados.Add(ResultadoComprobante.Error(string.Empty, string.Empty, ex.Message));
        }

        return new ResumenLote("Retenciones", inicio, DateTimeOffset.Now, _dryRun, resultados);
    }

    private short AmbienteEfectivo(short ambienteSesion) => _opciones.AmbienteForzado ?? ambienteSesion;

    // ===================== Núcleo compartido =====================

    private sealed record ContextoEmpresa(EmpresaEmisora Emisor, DatilEmpresa Datil, string CadenaSage);

    private async Task<ContextoEmpresa> CargarContextoAsync(string ruc, CancellationToken cancellationToken)
    {
        var emisor = await _empresas.EmisorAsync(ruc, cancellationToken);
        var datil = await _empresas.DatilAsync(ruc, cancellationToken);
        var cadena = await _conexionesSage.ResolverCadenaOdbcAsync(ruc, cancellationToken);
        return new ContextoEmpresa(emisor, datil, cadena);
    }

    /// <summary>
    /// Lee de Sage → arma → emite en Datil (o no, en DryRun) → guarda. Un solo
    /// comprobante; nunca lanza (el error queda en el resultado).
    /// </summary>
    private async Task<ResultadoComprobante> ProcesarCompraAsync(
        OdbcConnection conexion, ContextoEmpresa contexto, string ruc, short ambiente, string usuario,
        string piPostOrder, CancellationToken cancellationToken)
    {
        try
        {
            var leida = await LectorCompraRetencion.LeerAsync(conexion, piPostOrder, cancellationToken);
            if (leida is null)
            {
                return ResultadoComprobante.Error(piPostOrder, string.Empty,
                    $"[{piPostOrder}] no se encontró la compra en Sage 50.");
            }

            var resultado = await _builder.ArmarAsync(
                contexto.Emisor, leida.Compra, leida.Proveedor, ambiente, contexto.Datil.EmailPruebas, cancellationToken);

            if (!resultado.Ok)
            {
                return ResultadoComprobante.Error(piPostOrder, resultado.NumeroRetencion ?? string.Empty,
                    $"[{piPostOrder}] {string.Join(" | ", resultado.Errores)}");
            }

            var datil = await _datil.EmitirRetencionAsync(resultado.Retencion!, contexto.Datil.Credenciales, cancellationToken);

            if (datil.FueDryRun)
            {
                return new ResultadoComprobante(EstadoComprobante.DryRun, piPostOrder, resultado.NumeroRetencion ?? string.Empty,
                    null, null,
                    new[] { $"[{piPostOrder}] dry-run OK — retención {resultado.NumeroRetencion} (no se envió ni se guardó)." });
            }

            if (!datil.Emitido)
            {
                return ResultadoComprobante.Error(piPostOrder, resultado.NumeroRetencion ?? string.Empty,
                    $"[{piPostOrder}] Datil rechazó: {string.Join(" | ", datil.Errores)}");
            }

            var thid = await _repositorio.GuardarAsync(
                ruc, resultado, leida.Proveedor, ambiente, datil, usuario, cancellationToken);

            return new ResultadoComprobante(EstadoComprobante.Emitido, piPostOrder, resultado.NumeroRetencion ?? string.Empty,
                datil.Id, thid,
                new[] { $"[{piPostOrder}] retención {resultado.NumeroRetencion} emitida (Datil id {datil.Id}), THId {thid}." });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Retención {PiPostOrder} de {Ruc}", piPostOrder, ruc);
            return ResultadoComprobante.Error(piPostOrder, string.Empty, $"[{piPostOrder}] error: {ex.Message}");
        }
    }

    private static ResumenEmpresa Fallo(EmpresaActiva empresa, int pendientes, string mensaje) =>
        new(empresa.Ruc, empresa.Nombre, pendientes, 0, 1, new[] { mensaje });
}
