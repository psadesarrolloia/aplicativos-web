using PsaWeb.Modules.Reportes.Comun;

namespace PsaWeb.Modules.Reportes.Cheques;

/// <summary>
/// Junta datos de Sage + configuración de la empresa + renderizador para producir el PDF de cheques y
/// comprobantes de egreso (y la hoja de prueba). Lo usan los endpoints <c>/bancos/cheques/pdf</c> y
/// <c>/bancos/cheques/prueba</c> del Host.
/// </summary>
public sealed class ServicioImpresionCheques(
    IChequesRepository repositorio,
    IServicioConfiguracionReportes configuracion,
    ChequePdfRenderer renderizador)
{
    /// <summary>Máximo de pagos por PDF (evita URLs/PDF desmesurados; 1 hoja por pago).</summary>
    public const int MaximoPagos = 200;

    /// <param name="ruc">RUC explícito (endpoint) o <c>null</c> para la empresa de sesión.</param>
    /// <param name="nombreSesion">Nombre de la empresa según el shell (se usa si no hay uno configurado).</param>
    /// <returns>El PDF, o <c>null</c> si ninguno de los pagos pedidos existe.</returns>
    public async Task<byte[]?> GenerarPdfAsync(
        string? ruc,
        string nombreSesion,
        IReadOnlyList<long> postOrders,
        OpcionesImpresion opciones,
        CancellationToken cancellationToken = default)
    {
        if (!opciones.Valida)
        {
            throw new ArgumentException("Elija al menos «cheque» o «comprobante de egreso».", nameof(opciones));
        }
        if (postOrders.Count > MaximoPagos)
        {
            throw new ArgumentException($"Se pueden imprimir hasta {MaximoPagos} pagos por vez.", nameof(postOrders));
        }

        var armado = await ArmarAsync(ruc, nombreSesion, postOrders, opciones, cancellationToken);
        return armado is null ? null : renderizador.Generar(armado.Paginas, armado.Config, armado.Logo, armado.Titulo);
    }

    /// <summary>Vista previa: la primera hoja de los pagos pedidos como imagen PNG (o <c>null</c> si no hay pagos).</summary>
    public async Task<byte[]?> GenerarVistaPreviaAsync(
        string? ruc,
        string nombreSesion,
        IReadOnlyList<long> postOrders,
        OpcionesImpresion opciones,
        CancellationToken cancellationToken = default)
    {
        var armado = await ArmarAsync(ruc, nombreSesion, postOrders.Take(1).ToList(), opciones, cancellationToken);
        return armado is null ? null : renderizador.GenerarImagenes(armado.Paginas.Take(1), armado.Config, armado.Logo).FirstOrDefault();
    }

    private sealed record Armado(IReadOnlyList<PaginaCheque> Paginas, ConfiguracionCheque Config, LogoEmpresa? Logo, string Titulo);

    private async Task<Armado?> ArmarAsync(
        string? ruc, string nombreSesion, IReadOnlyList<long> postOrders, OpcionesImpresion opciones, CancellationToken ct)
    {
        var pagos = string.IsNullOrWhiteSpace(ruc)
            ? await repositorio.DetallesAsync(postOrders, ct)
            : await repositorio.DetallesParaRucAsync(ruc, postOrders, ct);
        if (pagos.Count == 0)
        {
            return null;
        }

        var (cfg, empresa, logo, nombre) = await CargarAsync(ruc, nombreSesion, ct);
        var paginas = pagos
            .SelectMany(p => ConstructorPaginaCheque.Construir(p, cfg, nombre, empresa.Ciudad, logo is not null, opciones))
            .ToList();
        return new Armado(paginas, cfg, logo, pagos.Count == 1 ? $"Pago {pagos[0].Pago.Referencia.Principal}" : "Cheques");
    }

    public async Task<byte[]> GenerarHojaPruebaAsync(string? ruc, string nombreSesion, CancellationToken cancellationToken = default)
    {
        var (cfg, empresa, logo, nombre) = await CargarAsync(ruc, nombreSesion, cancellationToken);
        var pagina = HojaPruebaCheque.Construir(cfg, nombre, empresa.Ciudad);
        return renderizador.Generar(new[] { pagina }, cfg, logo, "Hoja de prueba de impresión");
    }

    private async Task<(ConfiguracionCheque Cfg, ConfiguracionEmpresa Empresa, LogoEmpresa? Logo, string Nombre)> CargarAsync(
        string? ruc, string nombreSesion, CancellationToken ct)
    {
        var clave = string.IsNullOrWhiteSpace(ruc) ? "_sin-empresa" : ruc;
        var cfg = await configuracion.ObtenerAsync<ConfiguracionCheque>(clave, ClavesReporte.Cheques, ct);
        var empresa = await configuracion.ObtenerAsync<ConfiguracionEmpresa>(clave, ClavesReporte.Empresa, ct);
        var logo = await configuracion.LogoAsync(clave, ct);
        var nombre = string.IsNullOrWhiteSpace(empresa.NombreEmpresa) ? nombreSesion : empresa.NombreEmpresa.Trim();
        return (cfg, empresa, logo, nombre);
    }
}
