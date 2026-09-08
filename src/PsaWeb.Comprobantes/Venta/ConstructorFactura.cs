using PsaWeb.Comprobantes.Retenciones; // EmpresaEmisora, EstablecimientoInfo (DTOs genéricos)
using PsaWeb.Datil.Model;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>
/// Arma la <see cref="Factura"/> de Datil y los datos a persistir a partir de una
/// <see cref="FacturaVentaLeida"/>, el emisor y el establecimiento resueltos.
/// Port de <c>DatilSend(ref SaleInvoice)</c>. Lógica pura (sin ODBC ni EF).
/// </summary>
public static class ConstructorFactura
{
    /// <summary>Zona horaria de Ecuador (UTC-5, sin horario de verano).</summary>
    private static readonly TimeSpan ZonaEcuador = TimeSpan.FromHours(-5);

    public static ResultadoFactura Construir(
        EmpresaEmisora emisor,
        EstablecimientoInfo establecimiento,
        FacturaVentaLeida leida,
        short ambiente,
        string? emailPruebas,
        string codigoFormaPagoDatil,
        string moneda = "USD",
        short tipoEmision = 1)
    {
        var errores = new List<string>(leida.Errores);
        if (leida.Cliente is null)
        {
            errores.Add("La factura no tiene cliente.");
            return ResultadoFactura.ConErrores(errores);
        }
        errores.AddRange(leida.Cliente.Errores);

        var cab = leida.Cabecera;
        var cliente = leida.Cliente;

        if (cab.FechaEmision is null)
        {
            errores.Add("La factura no tiene fecha de emisión.");
        }

        // --- Email: en pruebas se redirige a la casilla de pruebas ---
        var email = cliente.Email;
        if (ambiente == 1 && !string.IsNullOrEmpty(emailPruebas) && emailPruebas!.Length > 1)
        {
            email = emailPruebas;
        }

        var comprador = new Comprador
        {
            RazonSocial = cliente.RazonSocial,
            Identificacion = cliente.Identificacion,
            TipoIdentificacion = cliente.TipoIdentificacion,
            Email = email,
            Direccion = cliente.Direccion,
            Telefono = cliente.Telefono,
        };

        var establecimientoDatil = new Establecimiento
        {
            Codigo = establecimiento.Codigo,
            PuntoEmision = cab.PuntoEmision, // el .exe usa el punto de emisión del NÚMERO, no el del establecimiento
            Direccion = establecimiento.Direccion,
        };

        var emisorDatil = new Emisor
        {
            Ruc = emisor.Ruc,
            RazonSocial = emisor.RazonSocial,
            NombreComercial = emisor.NombreComercial,
            Direccion = emisor.Direccion,
            ContribuyenteEspecial = emisor.ContribuyenteEspecial,
            ObligadoContabilidad = emisor.ObligadoContabilidad,
            Establecimiento = establecimientoDatil,
        };

        // --- Items ---
        var items = leida.Lineas.Select(l => new ItemComprobante
        {
            CodigoPrincipal = l.CodigoPrincipal,
            CodigoAuxiliar = l.CodigoAuxiliar,
            Descripcion = l.Descripcion,
            Cantidad = l.Cantidad,
            PrecioUnitario = l.PrecioUnitario,
            PrecioTotalSinImpuestos = l.SubtotalSinImpuestos,
            Descuento = l.Descuento,
            Impuestos =
            {
                new Impuesto
                {
                    Codigo = cab.CodigoIva,
                    CodigoPorcentaje = l.CodigoPorcentajeIva,
                    BaseImponible = l.BaseImponibleIva,
                    Valor = l.IvaValor,
                    Tarifa = TarifaPorcentaje(l.IvaPorcentaje),
                },
            },
        }).ToList();

        // --- Totales + impuestos agrupados ---
        var totales = new TotalesFactura
        {
            TotalSinImpuestos = cab.TotalSinImpuestos,
            ImporteTotal = cab.TotalConImpuestos,
            Propina = 0,
            Descuento = cab.DescuentoTotal,
            Impuestos = AgruparImpuestos(leida.Lineas, cab.CodigoIva),
        };

        // --- Factura ---
        var fechaEmision = new DateTimeOffset(cab.FechaEmision ?? default, ZonaEcuador);
        var factura = new Factura
        {
            Secuencial = SecuencialNumerico(cab.Secuencial),
            Moneda = moneda,
            Ambiente = ambiente,
            TipoEmision = tipoEmision,
            FechaEmision = fechaEmision,
            Emisor = emisorDatil,
            Comprador = comprador,
            Totales = totales,
            Items = items,
            InfoAdicional = leida.InfoAdicional.Count > 0 ? leida.InfoAdicional.ToList() : null,
        };

        // --- Pago al contado vs crédito ---
        if (cab.FechaVencimiento is null
            || (cab.FechaVencimiento.Value - (cab.FechaEmision ?? default)).Days < 1)
        {
            factura.Pagos.Add(new MetodoPago { Medio = codigoFormaPagoDatil, Total = cab.TotalConImpuestos });
        }
        else
        {
            factura.Credito = new CreditoFactura
            {
                Monto = cab.TotalConImpuestos,
                FechaVencimiento = cab.FechaVencimiento.Value.ToString("yyyy-MM-dd"),
            };
        }

        var persona = new PersonaParaGuardar(
            cliente.Identificacion, cliente.TipoIdentificacion, cliente.RazonSocial,
            cliente.Direccion, cliente.Email, cliente.Telefono, cliente.Fax);

        var lineasGuardar = leida.Lineas.Select(l => new LineaFacturaParaGuardar(
            l.Descripcion,
            string.IsNullOrEmpty(l.CodigoPrincipal) ? null : l.CodigoPrincipal,
            string.IsNullOrEmpty(l.CodigoAuxiliar) ? null : l.CodigoAuxiliar,
            l.Cantidad, l.PrecioUnitario, l.Descuento, l.SubtotalSinImpuestos,
            cab.CodigoIva, l.CodigoPorcentajeIva, l.BaseImponibleIva, l.IvaValor, l.IvaPorcentaje)).ToList();

        var guardar = new FacturaParaGuardar(
            CodDoc: "01",
            NumeroCompleto: cab.NumeroCompleto,
            Secuencial: cab.Secuencial,
            EstablecimientoId: establecimiento.EstablishmentId,
            Moneda: moneda,
            FechaEmision: cab.FechaEmision ?? default,
            FechaVencimiento: cab.FechaVencimiento,
            Ambiente: ambiente,
            IssueType: tipoEmision,
            TotalDescuento: cab.DescuentoTotal,
            CodigoIva: cab.CodigoIva,
            CodigoPorcentajeIva: cab.CodigoPorcentajeIva,
            TotalSinImpuestos: cab.TotalSinImpuestos,
            BaseImponibleIva: cab.BaseImponibleIva,
            IvaValor: cab.IvaValor,
            TotalConImpuestos: cab.TotalConImpuestos,
            PostOrderPeach: leida.PostOrderPeach,
            Persona: persona,
            Lineas: lineasGuardar);

        return new ResultadoFactura
        {
            Factura = factura,
            NumeroCompleto = cab.NumeroCompleto,
            EstablecimientoId = establecimiento.EstablishmentId,
            Guardar = guardar,
            Errores = errores,
        };
    }

    /// <summary>El % de IVA se envía a Datil en escala 0–100 (port de <c>CheckTaxPercentValue</c>).</summary>
    internal static double TarifaPorcentaje(double porcentaje) => porcentaje < 1 ? porcentaje * 100 : porcentaje;

    /// <summary>Quita ceros a la izquierda del secuencial (port de <c>Convert.ToInt64(FacturaNumber).ToString()</c>).</summary>
    internal static string SecuencialNumerico(string secuencial) =>
        long.TryParse(secuencial, out var n) ? n.ToString() : secuencial;

    internal static List<Impuesto> AgruparImpuestos(IReadOnlyList<FacturaVentaLinea> lineas, string codigoIva)
    {
        var grupos = lineas
            .GroupBy(l => new { l.CodigoPorcentajeIva, l.IvaPorcentaje })
            .Select(g => new
            {
                g.Key.CodigoPorcentajeIva,
                g.Key.IvaPorcentaje,
                Imponible = g.Sum(x => x.BaseImponibleIva),
            });

        var lista = new List<Impuesto>();
        foreach (var g in grupos)
        {
            var imponible = Math.Round((decimal)g.Imponible, 2);
            var valor = Math.Round(imponible * (decimal)g.IvaPorcentaje, 2);
            lista.Add(new Impuesto
            {
                Codigo = codigoIva,
                CodigoPorcentaje = g.CodigoPorcentajeIva,
                BaseImponible = (double)imponible,
                Valor = (double)valor,
            });
        }
        return lista;
    }
}
