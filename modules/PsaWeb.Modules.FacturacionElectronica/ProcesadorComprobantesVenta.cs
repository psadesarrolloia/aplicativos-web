using System.Data.Odbc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PsaWeb.Comprobantes.Retenciones;
using PsaWeb.Comprobantes.Venta;
using PsaWeb.Comprobantes.Venta.InfoAdicional;
using PsaWeb.Datil;
using PsaWeb.Modules.FacturacionElectronica.Data;
using PsaWeb.PeachEbills;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.FacturacionElectronica;

/// <summary>
/// Orquesta la generación de comprobantes de venta (facturas, notas de crédito y
/// liquidaciones de compra): lee de Sage 50, arma el documento, lo emite en Datil
/// (o no, en <c>DryRun</c>) y, si se emitió de verdad, lo persiste en PeachEBills.
/// Port del flujo de los <c>btnProcesToDatil_Click</c> / <c>ProcessBatch</c> del
/// <c>.exe</c>. Todas las operaciones reciben el RUC de la empresa (lo pasa la
/// página según la empresa de sesión).
/// </summary>
public sealed class ProcesadorComprobantesVenta
{
    private readonly EmisorLookup _emisores;
    private readonly PeachConnStringResolver _conexiones;
    private readonly ISageConnectionFactory _sageFactory;
    private readonly FacturaBuilder _facturaBuilder;
    private readonly NotaCreditoBuilder _ncBuilder;
    private readonly LiquidacionBuilder _liqBuilder;
    private readonly IConfigInfoAdicionalFactura _configInfo;
    private readonly ITasaIvaLookup _tasas;
    private readonly IDatilClient _datil;
    private readonly RepositorioComprobantesVenta _repositorio;
    private readonly FacturacionElectronicaOptions _opciones;
    private readonly bool _dryRun;
    private readonly ILogger<ProcesadorComprobantesVenta> _logger;

    public ProcesadorComprobantesVenta(
        EmisorLookup emisores,
        PeachConnStringResolver conexiones,
        ISageConnectionFactory sageFactory,
        FacturaBuilder facturaBuilder,
        NotaCreditoBuilder ncBuilder,
        LiquidacionBuilder liqBuilder,
        IConfigInfoAdicionalFactura configInfo,
        ITasaIvaLookup tasas,
        IDatilClient datil,
        RepositorioComprobantesVenta repositorio,
        IOptions<FacturacionElectronicaOptions> opciones,
        IOptions<DatilOptions> datilOpciones,
        ILogger<ProcesadorComprobantesVenta> logger)
    {
        _emisores = emisores;
        _conexiones = conexiones;
        _sageFactory = sageFactory;
        _facturaBuilder = facturaBuilder;
        _ncBuilder = ncBuilder;
        _liqBuilder = liqBuilder;
        _configInfo = configInfo;
        _tasas = tasas;
        _datil = datil;
        _repositorio = repositorio;
        _opciones = opciones.Value;
        _dryRun = datilOpciones.Value.DryRun;
        _logger = logger;
    }

    public bool DryRun => _dryRun;

    // ===================== LISTAS DE PENDIENTES =====================

    public Task<IReadOnlyList<FacturaPendiente>> ListarFacturasPendientesAsync(
        string ruc, DateTime desde, DateTime hasta, CancellationToken ct = default)
        => EnConexionAsync(ruc, (conexion, _, _) =>
            LectorFacturasPendientes.ListarAsync(conexion, desde, hasta, ct), ct);

    public Task<IReadOnlyList<NotaCreditoPendiente>> ListarNotasCreditoPendientesAsync(
        string ruc, DateTime desde, DateTime hasta, CancellationToken ct = default)
        => EnConexionAsync(ruc, (conexion, _, _) =>
            LectorNotasCreditoPendientes.ListarAsync(conexion, desde, hasta, ct), ct);

    public Task<IReadOnlyList<LiquidacionPendiente>> ListarLiquidacionesPendientesAsync(
        string ruc, DateTime desde, DateTime hasta, CancellationToken ct = default)
        => EnConexionAsync(ruc, (conexion, _, _) =>
            LectorLiquidacionesPendientes.ListarAsync(conexion, desde, hasta, ct), ct);

    // ===================== FACTURAS =====================

    public async Task<ResultadoComprobante> ProcesarFacturaAsync(
        string ruc, string postOrder, short ambiente, string usuario, CancellationToken ct = default)
        => await EnConexionAsync(ruc, async (conexion, emisor, datilEmpresa) =>
            await ProcesarFacturaInternaAsync(conexion, emisor, datilEmpresa, ruc, postOrder, ambiente, usuario, ct), ct);

    public Task<ResumenLote> ProcesarLoteFacturasAsync(
        string ruc, DateTime desde, DateTime hasta, short ambiente, string usuario, CancellationToken ct = default)
        => LoteAsync(ruc, "Facturas", ambiente, usuario,
            async conexion => (await LectorFacturasPendientes.ListarAsync(conexion, desde, hasta, ct))
                .Select(p => p.PostOrder).ToList(),
            (conexion, emisor, datilEmpresa, po) => ProcesarFacturaInternaAsync(conexion, emisor, datilEmpresa, ruc, po, ambiente, usuario, ct),
            ct);

    private async Task<ResultadoComprobante> ProcesarFacturaInternaAsync(
        OdbcConnection conexion, EmpresaEmisora emisor, DatilEmpresaFe datilEmpresa,
        string ruc, string postOrder, short ambiente, string usuario, CancellationToken ct)
    {
        try
        {
            var config = await _configInfo.ObtenerAsync(ruc, ct);
            var infoAdicional = await LectorInfoAdicional.ArmarAsync(conexion, postOrder, config, ct);

            var leida = await LectorFacturaVenta.LeerAsync(conexion, postOrder, ct, infoAdicional);
            if (leida is null)
            {
                return ResultadoComprobante.Error(postOrder, string.Empty, "No se encontró la factura en Sage 50.");
            }

            var armado = await _facturaBuilder.ArmarAsync(
                emisor, leida, ambiente, datilEmpresa.EmailPruebas, _opciones.FormaPagoContado, cancellationToken: ct);
            if (!armado.Ok)
            {
                return ResultadoComprobante.Error(postOrder, armado.NumeroCompleto, armado.Errores.ToArray());
            }

            var cred = new DatilCredentials(datilEmpresa.ApiKey, datilEmpresa.Password, datilEmpresa.FacturaUrl);
            var datil = await _datil.EmitirFacturaAsync(armado.Factura!, cred, ct);

            if (datil.FueDryRun)
            {
                return new ResultadoComprobante(EstadoComprobante.DryRun, postOrder, armado.NumeroCompleto, null, null,
                    new[] { "DRY-RUN: comprobante armado y validado, no se envió ni se guardó." });
            }
            if (!datil.Emitido)
            {
                return ResultadoComprobante.Error(postOrder, armado.NumeroCompleto,
                    ("Datil rechazó: " + string.Join(" | ", datil.Errores)));
            }

            var (factura, persona, detalles) = MapeadorEntidades.DesdeFactura(armado.Guardar!, ruc);
            factura.DatilId = datil.Id;
            var facturaId = await _repositorio.GuardarFacturaAsync(
                ruc, factura, persona, detalles, datil.RawResponse, usuario, ct);

            return new ResultadoComprobante(EstadoComprobante.Emitido, postOrder, armado.NumeroCompleto,
                datil.Id, facturaId, new[] { $"Emitida (Datil {datil.Id}), FacturaId {facturaId}." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Factura {PostOrder} de {Ruc}", postOrder, ruc);
            return ResultadoComprobante.Error(postOrder, string.Empty, ex.Message);
        }
    }

    // ===================== NOTAS DE CRÉDITO =====================

    public Task<ResultadoComprobante> ProcesarNotaCreditoAsync(
        string ruc, string postOrder, short ambiente, string usuario, CancellationToken ct = default)
        => EnConexionAsync(ruc, (conexion, emisor, datilEmpresa) =>
            ProcesarNotaCreditoInternaAsync(conexion, emisor, datilEmpresa, ruc, postOrder, ambiente, usuario, ct), ct);

    public Task<ResumenLote> ProcesarLoteNotasCreditoAsync(
        string ruc, DateTime desde, DateTime hasta, short ambiente, string usuario, CancellationToken ct = default)
        => LoteAsync(ruc, "Notas de crédito", ambiente, usuario,
            async conexion => (await LectorNotasCreditoPendientes.ListarAsync(conexion, desde, hasta, ct))
                .Select(p => p.PostOrder).ToList(),
            (conexion, emisor, datilEmpresa, po) => ProcesarNotaCreditoInternaAsync(conexion, emisor, datilEmpresa, ruc, po, ambiente, usuario, ct),
            ct);

    private async Task<ResultadoComprobante> ProcesarNotaCreditoInternaAsync(
        OdbcConnection conexion, EmpresaEmisora emisor, DatilEmpresaFe datilEmpresa,
        string ruc, string postOrder, short ambiente, string usuario, CancellationToken ct)
    {
        try
        {
            var leida = await LectorNotaCredito.LeerAsync(conexion, postOrder, ct);
            if (leida is null)
            {
                return ResultadoComprobante.Error(postOrder, string.Empty, "No se encontró la nota de crédito en Sage 50.");
            }

            var armado = await _ncBuilder.ArmarAsync(emisor, leida, ambiente, datilEmpresa.EmailPruebas, cancellationToken: ct);
            if (!armado.Ok)
            {
                return ResultadoComprobante.Error(postOrder, armado.NumeroCompleto, armado.Errores.ToArray());
            }

            var cred = new DatilCredentials(datilEmpresa.ApiKey, datilEmpresa.Password, datilEmpresa.NotaCreditoUrl);
            var datil = await _datil.EmitirNotaCreditoAsync(armado.NotaCredito!, cred, ct);

            if (datil.FueDryRun)
            {
                return new ResultadoComprobante(EstadoComprobante.DryRun, postOrder, armado.NumeroCompleto, null, null,
                    new[] { "DRY-RUN: nota de crédito armada, no se envió ni se guardó." });
            }
            if (!datil.Emitido)
            {
                return ResultadoComprobante.Error(postOrder, armado.NumeroCompleto,
                    "Datil rechazó: " + string.Join(" | ", datil.Errores));
            }

            var (nota, persona, detalles, detalleNc) = MapeadorEntidades.DesdeNotaCredito(armado.Guardar!, ruc);
            nota.DatilId = datil.Id;
            var facturaId = await _repositorio.GuardarNotaCreditoAsync(
                ruc, nota, persona, detalles, detalleNc, datil.RawResponse, usuario, ct);

            return new ResultadoComprobante(EstadoComprobante.Emitido, postOrder, armado.NumeroCompleto,
                datil.Id, facturaId, new[] { $"Emitida (Datil {datil.Id}), FacturaId {facturaId}." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nota de crédito {PostOrder} de {Ruc}", postOrder, ruc);
            return ResultadoComprobante.Error(postOrder, string.Empty, ex.Message);
        }
    }

    // ===================== LIQUIDACIONES DE COMPRA =====================

    public Task<ResultadoComprobante> ProcesarLiquidacionAsync(
        string ruc, string postOrder, short ambiente, string usuario, CancellationToken ct = default)
        => EnConexionAsync(ruc, (conexion, emisor, datilEmpresa) =>
            ProcesarLiquidacionInternaAsync(conexion, emisor, datilEmpresa, ruc, postOrder, ambiente, usuario, ct), ct);

    public Task<ResumenLote> ProcesarLoteLiquidacionesAsync(
        string ruc, DateTime desde, DateTime hasta, short ambiente, string usuario, CancellationToken ct = default)
        => LoteAsync(ruc, "Liquidaciones de compra", ambiente, usuario,
            async conexion => (await LectorLiquidacionesPendientes.ListarAsync(conexion, desde, hasta, ct))
                .Select(p => p.PostOrder).ToList(),
            (conexion, emisor, datilEmpresa, po) => ProcesarLiquidacionInternaAsync(conexion, emisor, datilEmpresa, ruc, po, ambiente, usuario, ct),
            ct);

    private async Task<ResultadoComprobante> ProcesarLiquidacionInternaAsync(
        OdbcConnection conexion, EmpresaEmisora emisor, DatilEmpresaFe datilEmpresa,
        string ruc, string postOrder, short ambiente, string usuario, CancellationToken ct)
    {
        try
        {
            var leida = await LectorLiquidacionCompra.LeerAsync(conexion, postOrder, _tasas, ct);
            if (leida is null)
            {
                return ResultadoComprobante.Error(postOrder, string.Empty, "No se encontró la liquidación en Sage 50.");
            }

            var armado = await _liqBuilder.ArmarAsync(emisor, leida, ambiente, datilEmpresa.EmailPruebas, cancellationToken: ct);
            if (!armado.Ok)
            {
                return ResultadoComprobante.Error(postOrder, armado.NumeroCompleto, armado.Errores.ToArray());
            }

            var cred = new DatilCredentials(datilEmpresa.ApiKey, datilEmpresa.Password, datilEmpresa.LiquidacionUrl);
            var datil = await _datil.EmitirLiquidacionAsync(armado.Liquidacion!, cred, ct);

            if (datil.FueDryRun)
            {
                return new ResultadoComprobante(EstadoComprobante.DryRun, postOrder, armado.NumeroCompleto, null, null,
                    new[] { "DRY-RUN: liquidación armada, no se envió ni se guardó." });
            }
            if (!datil.Emitido)
            {
                return ResultadoComprobante.Error(postOrder, armado.NumeroCompleto,
                    "Datil rechazó: " + string.Join(" | ", datil.Errores));
            }

            var (factura, persona, detalles) = MapeadorEntidades.DesdeLiquidacion(armado.Guardar!, ruc);
            factura.DatilId = datil.Id;
            var facturaId = await _repositorio.GuardarFacturaAsync(
                ruc, factura, persona, detalles, datil.RawResponse, usuario, ct);

            return new ResultadoComprobante(EstadoComprobante.Emitido, postOrder, armado.NumeroCompleto,
                datil.Id, facturaId, new[] { $"Emitida (Datil {datil.Id}), FacturaId {facturaId}." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Liquidación {PostOrder} de {Ruc}", postOrder, ruc);
            return ResultadoComprobante.Error(postOrder, string.Empty, ex.Message);
        }
    }

    // ===================== infraestructura =====================

    private async Task<T> EnConexionAsync<T>(
        string ruc, Func<OdbcConnection, EmpresaEmisora, DatilEmpresaFe, Task<T>> accion, CancellationToken ct)
    {
        var emisor = await _emisores.EmisorAsync(ruc, ct);
        var datilEmpresa = await _emisores.DatilAsync(ruc, ct);
        var cadena = await _conexiones.ResolverCadenaOdbcAsync(ruc, ct);

        await using var conexion = _sageFactory.CreateConnection(cadena);
        await conexion.OpenAsync(ct);
        return await accion(conexion, emisor, datilEmpresa);
    }

    private async Task<ResumenLote> LoteAsync(
        string ruc, string tipoDoc, short ambiente, string usuario,
        Func<OdbcConnection, Task<List<string>>> listarPendientes,
        Func<OdbcConnection, EmpresaEmisora, DatilEmpresaFe, string, Task<ResultadoComprobante>> procesarUno,
        CancellationToken ct)
    {
        var inicio = DateTimeOffset.Now;
        var resultados = new List<ResultadoComprobante>();

        await EnConexionAsync(ruc, async (conexion, emisor, datilEmpresa) =>
        {
            var pendientes = await listarPendientes(conexion);
            foreach (var po in pendientes)
            {
                ct.ThrowIfCancellationRequested();
                resultados.Add(await procesarUno(conexion, emisor, datilEmpresa, po));
            }
            return 0;
        }, ct);

        return new ResumenLote(tipoDoc, inicio, DateTimeOffset.Now, _dryRun, resultados);
    }
}
