using PsaWeb.Ats.Esquema;

namespace PsaWeb.Modules.Ats.Data;

/// <summary>
/// Datos ficticios para desarrollo sin tocar Sage 50 (p. ej. en PREDATOR contra
/// el DSN de ejemplo, que no tiene las tablas del ATS). Deja la pantalla 100%
/// funcional: 2 establecimientos, 1 factura y 1 NC de venta, 1 factura y 1
/// liquidación de compra (con retención de renta), y 1 anulado.
/// </summary>
internal sealed class SampleAtsRepository : IAtsRepository
{
    public Task<ivaType> GenerarAsync(FiltroAts filtro, CancellationToken cancellationToken = default)
        => Task.FromResult(Generar(filtro));

    public Task<ivaType> GenerarParaRucAsync(string ruc, FiltroAts filtro, CancellationToken cancellationToken = default)
        => Task.FromResult(Generar(filtro));

    private static ivaType Generar(FiltroAts filtro)
    {
        var ventas = new[]
        {
            new detalleVentasType
            {
                tpIdCliente = "05", idCliente = "1712345678", tipoComprobante = "18",
                numeroComprobantes = "3", baseNoGraIva = 0m, baseImponible = 0m,
                baseImpGrav = 5000m, montoIva = 750m, montoIce = 0m, montoIceSpecified = true,
                valorRetIva = 0m, valorRetRenta = 0m, formasDePago = new[] { "20" },
            },
            new detalleVentasType
            {
                tpIdCliente = "05", idCliente = "1798765432", tipoComprobante = "04",
                numeroComprobantes = "1", baseNoGraIva = 0m, baseImponible = 0m,
                baseImpGrav = 300m, montoIva = 45m, montoIce = 0m, montoIceSpecified = true,
                valorRetIva = 0m, valorRetRenta = 0m,
            },
        };

        var compras = new[]
        {
            new detalleComprasType
            {
                codSustento = "01", tpIdProv = "01", idProv = "1790000000001", tipoComprobante = "01",
                establecimiento = "001", puntoEmision = "001", secuencial = "000001234",
                fechaEmision = $"01/{filtro.Mes:00}/{filtro.Anio}",
                baseNoGraIva = 0m, baseImponible = 0m, baseImpGrav = 2000m, baseImpExe = 0m,
                montoIce = 0m, montoIva = 300m,
                air = new[]
                {
                    new detalleAirComprasType { codRetAir = "303", baseImpAir = 2000m, porcentajeAir = 1m, valRetAir = 20m },
                },
            },
        };

        var ventasEstablecimiento = new[]
        {
            new ventaEstType { codEstab = "001", ventasEstab = 5000m, ivaComp = 0m, ivaCompSpecified = true },
            new ventaEstType { codEstab = "002", ventasEstab = 0m, ivaComp = 0m, ivaCompSpecified = true },
        };

        var anulados = new[]
        {
            new detalleAnuladosType
            {
                tipoComprobante = "18", establecimiento = "001", puntoEmision = "001",
                secuencialInicio = "000001200", secuencialFin = "000001200", autorizacion = "9999999999",
            },
        };

        return new ivaType
        {
            IdInformante = "1790000000001",
            razonSocial = "EMPRESA DE MUESTRA S A",
            Anio = filtro.Anio.ToString(),
            Mes = filtro.Mes.ToString("00"),
            numEstabRuc = "002",
            totalVentas = 5000m + 300m,
            totalVentasSpecified = true,
            ventas = ventas,
            ventasEstablecimiento = ventasEstablecimiento,
            compras = compras,
            anulados = anulados,
        };
    }
}
