namespace PsaWeb.Ats.Ventas.Muestra;

/// <summary>
/// Filas de ventas de muestra para desarrollar/mostrar el módulo sin tocar
/// Sage 50 (PREDATOR no siempre llega a la empresa multi-compañía de
/// `SERWEBPSA01`). Ya pasadas por <see cref="ArmadorVentasAts"/> quedan listas
/// para el esquema.
/// </summary>
public static class VentasMuestraAts
{
    public static IReadOnlyList<FilaVentaCruda> Filas() => new[]
    {
        // Factura a cliente nacional (RUC), con IVA y una retención recibida.
        new FilaVentaCruda(
            TipoComprobante: "18",
            NumeroComprobantes: "3",
            Cliente: new ClienteAts(TiposIdentificacionClienteAts.Ruc, "1790011110001", string.Empty, string.Empty),
            Buckets: new BucketsVentaAts(
                BaseNoGraIva: 0m,
                BaseImponibleCruda: 500m, // se descarta (Bug B1): el esquema lleva 0.
                BaseImpGrav: 1200.00m,
                MontoIva: 144.00m,
                ValorRetIva: 14.40m,
                ValorRetRenta: 12.00m)),

        // Factura a cliente del exterior (sin retenciones).
        new FilaVentaCruda(
            TipoComprobante: "18",
            NumeroComprobantes: "1",
            Cliente: new ClienteAts(TiposIdentificacionClienteAts.Exterior, "PA1234567", "02", "CLIENTE DEL EXTERIOR S.A."),
            Buckets: new BucketsVentaAts(
                BaseNoGraIva: 0m,
                BaseImponibleCruda: 0m,
                BaseImpGrav: 800.00m,
                MontoIva: 0m,
                ValorRetIva: 0m,
                ValorRetRenta: 0m)),

        // Nota de crédito sobre una factura anterior al mismo cliente nacional.
        new FilaVentaCruda(
            TipoComprobante: "04",
            NumeroComprobantes: "1",
            Cliente: new ClienteAts(TiposIdentificacionClienteAts.Ruc, "1790011110001", string.Empty, string.Empty),
            Buckets: new BucketsVentaAts(
                BaseNoGraIva: 0m,
                BaseImponibleCruda: 0m,
                BaseImpGrav: 200.00m,
                MontoIva: 24.00m,
                ValorRetIva: 0m,
                ValorRetRenta: 0m)),
    };
}
