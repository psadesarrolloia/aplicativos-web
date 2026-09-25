namespace PsaWeb.Compras.Sri;

/// <summary>
/// Factura electrónica recibida de un proveedor, tal como la lee el `.exe` desde el XML autorizado del SRI
/// (port de <c>ImportXmlLib.Model.FacturaE.LoadSaleInvoice</c>, que la volcaba en <c>DatilClientLibrary.Factura</c>).
/// Los importes van en <see cref="decimal"/> (el `.exe` usaba <c>double</c>; ver §8 del plan de la Ola 2).
/// </summary>
public sealed record FacturaRecibida(
    int Ambiente,
    string ClaveAcceso,
    string Establecimiento,
    string PuntoEmision,
    string Secuencial,
    DateTime FechaEmision,
    EmisorFactura Emisor,
    CompradorFactura Comprador,
    IReadOnlyList<DetalleFactura> Detalles,
    TotalesFactura Totales,
    IReadOnlyList<PagoFactura>? Pagos)
{
    /// <summary><c>estab-ptoEmi-secuencial</c> (<c>001-001-000000123</c>): va a <c>TermsDescription</c> y <c>ShipToAddress1</c> de la OC.</summary>
    public string NumeroCompleto => $"{Establecimiento}-{PuntoEmision}-{Secuencial}";

    /// <summary>Ambiente 1 = pruebas (el `.exe` lo pinta en azul; la web advierte).</summary>
    public bool EsAmbientePruebas => Ambiente == 1;
}

public sealed record EmisorFactura(
    string Ruc,
    string RazonSocial,
    string NombreComercial,
    string DireccionMatriz,
    string DireccionEstablecimiento,
    string? ContribuyenteEspecial,
    bool ObligadoContabilidad);

public sealed record CompradorFactura(string Identificacion, string TipoIdentificacion, string RazonSocial, string? Direccion);

public sealed record DetalleFactura(
    string CodigoPrincipal,
    string CodigoAuxiliar,
    string Descripcion,
    decimal Cantidad,
    decimal PrecioUnitario,
    decimal Descuento,
    decimal PrecioTotalSinImpuesto,
    IReadOnlyList<ImpuestoDetalle> Impuestos);

public sealed record ImpuestoDetalle(string Codigo, string CodigoPorcentaje, decimal Tarifa, decimal BaseImponible, decimal Valor);

/// <param name="Propina">Propina de <c>infoFactura</c> más los <c>otrosRubrosTerceros</c> (el `.exe` los suma a la propina).</param>
/// <param name="Impuestos">Solo los de <c>valor &gt; 0</c>, como el `.exe`.</param>
public sealed record TotalesFactura(
    decimal TotalSinImpuestos,
    decimal TotalDescuento,
    decimal Propina,
    decimal ImporteTotal,
    IReadOnlyList<ImpuestoTotal> Impuestos);

/// <param name="Tarifa">
/// El total no trae tarifa: el `.exe` la toma del primer detalle con el mismo código y código de porcentaje;
/// <c>null</c> si ningún detalle calza.
/// </param>
public sealed record ImpuestoTotal(string Codigo, string CodigoPorcentaje, decimal BaseImponible, decimal Valor, decimal? Tarifa);

public sealed record PagoFactura(string FormaPago, decimal Total);
