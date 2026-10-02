namespace PsaWeb.Ventas.Prefacturas;

public enum EstadoPrefactura
{
    /// <summary>Emitida por el vendedor; contabilidad todavía no la digitó en Sage.</summary>
    Emitida = 0,

    /// <summary>Contabilidad ya la digitó en Sage y anotó el número de la factura.</summary>
    Facturada = 1,

    /// <summary>Descartada (el cliente no la aceptó o se rehízo).</summary>
    Anulada = 2,
}

public enum EstadoCorreo
{
    /// <summary>Aún no se intentó (o se está enviando).</summary>
    Pendiente = 0,
    Enviado = 1,

    /// <summary>El SMTP respondió con error; se puede reenviar.</summary>
    Fallo = 2,

    /// <summary>No hay SMTP configurado en el servidor, o la empresa no tiene destinatarios.</summary>
    NoConfigurado = 3,
}

/// <summary>Una línea tal como la digita el vendedor (el monto lo calcula <see cref="CalculadoraPrefactura"/>).</summary>
/// <param name="PrecioLista">Precio de la lista de Sage del cliente al emitir; <c>null</c> si esa lista no tiene precio.</param>
public sealed record LineaSolicitada(
    string ItemId,
    string Descripcion,
    string UnidadMedida,
    decimal Cantidad,
    decimal? PrecioLista,
    decimal PrecioUnitario,
    decimal? ExistenciaAlEmitir)
{
    /// <summary>true si el vendedor tecleó un precio distinto del de la lista (sin aprobación, decisión del usuario 2026-10-02).</summary>
    public bool PrecioManual => PrecioLista is null || PrecioLista.Value != PrecioUnitario;
}

/// <summary>Todo lo que el vendedor elige o digita al emitir una prefactura.</summary>
public sealed record SolicitudPrefactura(
    string ClienteId,
    string ClienteNombre,
    string ClienteContacto,
    string ClienteTelefono,
    string ClienteEmail,
    int ListaDePrecios,
    int DiasCredito,
    decimal LimiteCredito,
    decimal SaldoCliente,
    string Vendedor,
    string Etiqueta,
    string OrdenCliente,
    string DireccionEnvio,
    string NotaCliente,
    string NotaInterna,
    bool AplicaIva,
    IReadOnlyList<LineaSolicitada> Lineas);

/// <summary>Una línea ya calculada.</summary>
public sealed record LineaPrefactura(
    int Orden,
    string ItemId,
    string Descripcion,
    string UnidadMedida,
    decimal Cantidad,
    decimal? PrecioLista,
    decimal PrecioUnitario,
    bool PrecioManual,
    decimal Monto,
    decimal? ExistenciaAlEmitir);

/// <summary>Prefactura emitida: la que se guarda, se descarga en PDF y se envía a contabilidad para digitarla en Sage.</summary>
public sealed record Prefactura(
    int Id,
    string Ruc,
    string EmpresaNombre,
    int Numero,
    DateOnly FechaEmision,
    DateOnly ValidaHasta,
    EstadoPrefactura Estado,
    string ClienteId,
    string ClienteNombre,
    string ClienteContacto,
    string ClienteTelefono,
    string ClienteEmail,
    int ListaDePrecios,
    int DiasCredito,
    string Terminos,
    string Vendedor,
    string Etiqueta,
    string OrdenCliente,
    string DireccionEnvio,
    string NotaCliente,
    string NotaInterna,
    string CodigoImpuesto,
    decimal PorcentajeIva,
    decimal Subtotal,
    decimal Iva,
    decimal Total,
    IReadOnlyList<LineaPrefactura> Lineas,
    string CreadaPor,
    DateTime CreadaEn,
    EstadoCorreo CorreoEstado,
    string CorreoDestinatarios,
    string? CorreoError,
    DateTime? CorreoEnviadoEn,
    string? FacturaSage,
    string? FacturadaPor,
    DateTime? FacturadaEn)
{
    /// <summary>Número visible: <c>PF-0001</c>. Es interno, <b>no</b> el de la factura del SRI.</summary>
    public string NumeroTexto => FormatoNumero(Numero);

    public static string FormatoNumero(int numero) => "PF-" + numero.ToString("0000", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Vencida: emitida, sin facturar y pasada su vigencia.</summary>
    public bool EstaVencida(DateOnly hoy) => Estado == EstadoPrefactura.Emitida && hoy > ValidaHasta;

    public string EstadoVisible(DateOnly hoy) => Estado switch
    {
        EstadoPrefactura.Facturada => "Facturada",
        EstadoPrefactura.Anulada => "Anulada",
        _ => EstaVencida(hoy) ? "Vencida" : "Vigente",
    };
}

/// <summary>Configuración por empresa del portal de ventas.</summary>
/// <param name="CorreoContabilidad">Destinatario 1: la persona de Contabilidad que digita la factura en Sage.</param>
/// <param name="CorreoAdicional">Destinatario 2 (opcional).</param>
public sealed record ConfiguracionVentas(
    string Ruc,
    string CorreoContabilidad,
    string CorreoAdicional,
    int VigenciaDias,
    string CodigoImpuesto,
    decimal PorcentajeIva)
{
    public const int VigenciaPorDefecto = 15;
    public const string CodigoImpuestoPorDefecto = "4-15%";
    public const decimal PorcentajeIvaPorDefecto = 15m;

    public static ConfiguracionVentas PorDefecto(string ruc) =>
        new(ruc, string.Empty, string.Empty, VigenciaPorDefecto, CodigoImpuestoPorDefecto, PorcentajeIvaPorDefecto);

    /// <summary>Los 1–2 destinatarios configurados, sin vacíos ni repetidos.</summary>
    public IReadOnlyList<string> Destinatarios() =>
        new[] { CorreoContabilidad, CorreoAdicional }
            .Select(c => c?.Trim() ?? string.Empty)
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
