using PsaWeb.Ats.Esquema;

namespace PsaWeb.Ats.Compras.Muestra;

/// <summary>
/// Compras de muestra para desarrollar/mostrar el módulo sin tocar Sage 50.
/// Ya armadas por <see cref="ArmadorComprasAts"/>.
/// </summary>
public static class ComprasMuestraAts
{
    public static IReadOnlyList<detalleComprasType> Filas() => new[]
    {
        ArmadorComprasAts.Armar(FacturaConRetencion()),
        ArmadorComprasAts.Armar(NotaCredito()),
        ArmadorComprasAts.Armar(Liquidacion()),
    };

    private static ProveedorAts ProveedorNacional(string id = "1790011110001", string nombre = "PROVEEDOR DEMO S.A.") =>
        new(true, TiposIdentificacionProveedorAts.Ruc, id, nombre, string.Empty,
            InfoProveedorExterior.ArmarPagoExterior(InfoProveedorExterior.Ninguna), null);

    private static CompraCruda FacturaConRetencion() => new(
        TipoComprobante: TiposComprobanteComprasAts.Factura,
        CodSustento: CodigosSustentoAts.Credito,
        Establecimiento: "001",
        PuntoEmision: "001",
        Secuencial: "000000012",
        FechaRegistro: "05/09/2026",
        FechaEmision: "05/09/2026",
        Autorizacion: "0509202601179001111000120010010000000121642330011",
        Proveedor: ProveedorNacional(),
        Buckets: new BucketsComprasAts(
            BaseNoGraIva: 0m, BaseImponible: 1996.00m, BaseImpGrav: 0m, BaseImpExe: 0m, MontoIva: 0m,
            ValRetBien10: 0m, ValRetServ20: 0m, ValorRetBienes: 0m, ValRetServ50: 0m, ValorRetServicios: 0m, ValRetServ100: 0m),
        ShipToAddress2: "001-001-000000045",
        ShipToCity: "0509202601179001111000120010010000000451234567890",
        RetencionesRenta: new[]
        {
            new detalleAirComprasType { codRetAir = "310", baseImpAir = 1996.00m, porcentajeAir = 1.00m, valRetAir = 19.96m },
        },
        NumeroCompletoCompraOriginal: null,
        AutorizacionCompraOriginal: null);

    private static CompraCruda NotaCredito() => new(
        TipoComprobante: TiposComprobanteComprasAts.NotaCredito,
        CodSustento: CodigosSustentoAts.Credito,
        Establecimiento: "003",
        PuntoEmision: "001",
        Secuencial: "000000163",
        FechaRegistro: "24/09/2026",
        FechaEmision: "24/09/2026",
        Autorizacion: string.Empty,
        Proveedor: ProveedorNacional("1792278767001", "OTRO PROVEEDOR S.A."),
        Buckets: new BucketsComprasAts(0m, 110.00m, 0m, 0m, 0m, 0, 0, 0, 0, 0, 0),
        ShipToAddress2: string.Empty,
        ShipToCity: string.Empty,
        RetencionesRenta: null,
        NumeroCompletoCompraOriginal: "003-001-000011279",
        AutorizacionCompraOriginal: "1509202601179227876700120030010000112792050004610");

    private static CompraCruda Liquidacion() => new(
        TipoComprobante: TiposComprobanteComprasAts.Liquidacion,
        CodSustento: CodigosSustentoAts.Credito,
        Establecimiento: "001",
        PuntoEmision: "001",
        Secuencial: "000003419",
        FechaRegistro: "10/09/2026",
        FechaEmision: "10/09/2026",
        Autorizacion: "9999999999",
        Proveedor: new ProveedorAts(
            true, TiposIdentificacionProveedorAts.Exterior, "PA1234567", "PROVEEDOR DEL EXTERIOR",
            "02", InfoProveedorExterior.ArmarPagoExterior(new InfoProveedorExterior(
                TipoProveedorExterior.Sociedad, TipoPagoExterior.Exterior, "01", "331", "331", RespuestaSiNo.No, RespuestaSiNo.Ninguna)),
            null),
        Buckets: new BucketsComprasAts(0m, 0m, 19215.00m, 0m, 2882.25m, 0, 0, 0, 0, 0, 0),
        ShipToAddress2: string.Empty,
        ShipToCity: string.Empty,
        RetencionesRenta: null,
        NumeroCompletoCompraOriginal: null,
        AutorizacionCompraOriginal: null);
}
