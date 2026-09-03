using System.Globalization;
using PsaWeb.Comprobantes.Proveedores;
using PsaWeb.Comprobantes.Sri;
using PsaWeb.Datil.Model;

namespace PsaWeb.Comprobantes.Retenciones;

/// <summary>
/// Arma una <see cref="Retencion"/> para Datil a partir de los datos ya leídos de
/// Sage 50 y de la base, aplicando las validaciones del <c>LoadPurchaseTwh</c> /
/// <c>DatilSend</c> originales. Es lógica pura y determinística (sin ODBC ni SQL).
/// </summary>
public static class ConstructorRetencion
{
    /// <summary>Zona horaria de Ecuador (UTC-5, sin horario de verano).</summary>
    private static readonly TimeSpan ZonaEcuador = TimeSpan.FromHours(-5);

    public static ResultadoRetencion Construir(
        EmpresaEmisora emisor,
        DatosCompraRetencion compra,
        ProveedorSri proveedor,
        EstablecimientoInfo? establecimiento,
        short ambiente,
        string emailDestino,
        IReadOnlyDictionary<string, string>? informacionAdicional)
    {
        var errores = new List<string>();

        // --- Número de retención (formato de factura, tolerante) → secuencial ---
        var numRet = NumeroDocumentoSri.AnalizarFactura(compra.NumeroRetencion);
        if (!numRet.EsValido)
        {
            errores.Add($"Formato no válido del número de retención: '{compra.NumeroRetencion}'.");
        }

        if (establecimiento is null)
        {
            errores.Add(
                $"No se encontró establecimiento registrado para {numRet.CodigoEstablecimiento}-{numRet.PuntoEmision}.");
        }

        // --- Número de la factura sustento (formato estricto) ---
        if (!NumeroDocumentoSri.AnalizarEstricto(compra.NumeroFactura).EsValido)
        {
            errores.Add($"Número de factura de compra incorrecto: '{compra.NumeroFactura}'.");
        }

        errores.AddRange(proveedor.Errores);

        // --- Fecha de emisión = GoodThruDate de la OC, si no la fecha de compra ---
        if (compra.GoodThruDate is null)
        {
            errores.Add("Fecha 'Good Thru' no asignada en la Purchase Order; no se puede emitir la retención.");
        }
        var fechaEmision = (compra.GoodThruDate ?? compra.FechaCompra).Date;

        // --- Tipo de comprobante sustento ---
        var codDocSustento = CodigosDocumento.CodigoDe(compra.ShipVia);
        if (codDocSustento is null)
        {
            errores.Add($"Emite retención a un tipo de comprobante no reconocido ('{compra.ShipVia}').");
        }
        else if (proveedor.TipoIdentificacion is TiposIdentificacion.Exterior or TiposIdentificacion.Pasaporte
                 && codDocSustento is not "03") // 03 = liquidación
        {
            errores.Add("No se admite emitir retención a este tipo de proveedor sobre una factura o nota de venta.");
        }

        // --- Ítems ---
        var items = new List<ItemRetencion>();
        foreach (var linea in compra.Lineas)
        {
            var (item, erroresLinea) = MapearLinea(linea, compra, codDocSustento ?? string.Empty);
            errores.AddRange(erroresLinea);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        var totalRetenido = items.Sum(x => x.ValorRetenido);
        if (codDocSustento is "02" && totalRetenido > 0) // 02 = nota de venta
        {
            errores.Add("No se admite emitir una retención con valor mayor a 0 sobre una Nota de Venta.");
        }

        var retencion = ArmarComprobante(
            emisor, proveedor, establecimiento, ambiente, emailDestino,
            numRet.Secuencial, fechaEmision, compra.FechaCompra, items, informacionAdicional);

        return new ResultadoRetencion
        {
            Retencion = retencion,
            NumeroRetencion = numRet.Completo.Length > 0 ? numRet.Completo : compra.NumeroRetencion,
            PostOrderPeach = compra.PostOrderPeach,
            Secuencial = numRet.Secuencial,
            PeriodoFiscal = PeriodoFiscal(compra.FechaCompra),
            FechaEmision = fechaEmision,
            EstablishmentId = establecimiento?.EstablishmentId,
            ContactoId = proveedor.Identificacion,
            TotalRetenido = totalRetenido,
            Errores = errores,
        };
    }

    private static (ItemRetencion? item, IReadOnlyList<string> errores) MapearLinea(
        LineaRetencionCruda linea, DatosCompraRetencion compra, string codDocSustento)
    {
        var errores = new List<string>();

        var code = linea.Category.Contains("RF", StringComparison.OrdinalIgnoreCase) ? "1" : "2";
        var baseImponible = Math.Abs(Math.Round((double)linea.Quantity, 2));
        var valorRetenido = Math.Abs(Math.Round((double)linea.Amount, 2));

        double porcentaje = 0;
        if (!linea.CustomField2.Contains('%'))
        {
            errores.Add($"Ítem con CustomField2 mal configurado (ItemID {linea.ItemId}): no contiene '%'.");
        }
        else
        {
            var textoPct = linea.CustomField2.Split('%')[0];
            if (decimal.TryParse(textoPct, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct)
                || decimal.TryParse(textoPct, NumberStyles.Any, CultureInfo.CurrentCulture, out pct))
            {
                porcentaje = Math.Abs(Math.Round((double)pct, 2));
            }
            else
            {
                errores.Add($"Ítem con CustomField2 mal configurado (ItemID {linea.ItemId}): '{linea.CustomField2}'.");
            }
        }

        var item = new ItemRetencion
        {
            Codigo = code,
            CodigoPorcentaje = linea.CustomField1,
            Porcentaje = porcentaje,
            BaseImponible = baseImponible,
            ValorRetenido = valorRetenido,
            FechaEmisionDocumentoSustento = new DateTimeOffset(compra.FechaCompra.Date, ZonaEcuador),
            NumeroDocumentoSustento = compra.NumeroFactura,
            TipoDocumentoSustento = codDocSustento,
        };

        return (item, errores);
    }

    private static Retencion ArmarComprobante(
        EmpresaEmisora emisor, ProveedorSri proveedor, EstablecimientoInfo? establecimiento,
        short ambiente, string emailDestino, string secuencial,
        DateTime fechaEmision, DateTime fechaCompra, List<ItemRetencion> items,
        IReadOnlyDictionary<string, string>? informacionAdicional)
    {
        var establecimientoDoc = new Establecimiento
        {
            Codigo = establecimiento?.Codigo ?? string.Empty,
            PuntoEmision = establecimiento?.PuntoEmision ?? string.Empty,
            Direccion = establecimiento?.Direccion ?? emisor.Direccion,
        };

        return new Retencion
        {
            Secuencial = long.TryParse(secuencial, out var s) ? s.ToString(CultureInfo.InvariantCulture) : secuencial,
            FechaEmision = new DateTimeOffset(fechaEmision, ZonaEcuador),
            Ambiente = ambiente,
            TipoEmision = 1,
            PeriodoFiscal = PeriodoFiscal(fechaCompra),
            Emisor = new Emisor
            {
                Ruc = emisor.Ruc,
                RazonSocial = emisor.RazonSocial,
                NombreComercial = emisor.NombreComercial,
                Direccion = emisor.Direccion,
                ContribuyenteEspecial = emisor.ContribuyenteEspecial,
                ObligadoContabilidad = emisor.ObligadoContabilidad,
                Establecimiento = establecimientoDoc,
            },
            Sujeto = new Comprador
            {
                RazonSocial = proveedor.RazonSocial,
                Identificacion = proveedor.Identificacion,
                TipoIdentificacion = proveedor.TipoIdentificacion,
                Email = emailDestino,
                Direccion = proveedor.Direccion,
                Telefono = proveedor.Telefono,
            },
            Items = items,
            InformacionAdicional = informacionAdicional is { Count: > 0 }
                ? new Dictionary<string, string>(informacionAdicional)
                : null,
        };
    }

    private static string PeriodoFiscal(DateTime fecha) =>
        $"{fecha.Month:00}/{fecha.Year}";
}
