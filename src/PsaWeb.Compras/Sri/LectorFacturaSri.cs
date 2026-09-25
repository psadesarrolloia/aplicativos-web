using System.Globalization;
using System.Xml.Linq;
using static PsaWeb.Compras.Sri.LectorComprobanteSri;

namespace PsaWeb.Compras.Sri;

public sealed record ResultadoLecturaFactura(FacturaRecibida? Factura, TipoComprobanteSri Tipo, string? Error);

/// <summary>Lee una factura del SRI. Port de <c>ImportXmlLib.Model.FacturaE.LoadSaleInvoice</c>.</summary>
public static class LectorFacturaSri
{
    public static ResultadoLecturaFactura Leer(string contenidoXml)
    {
        var comprobante = LectorComprobanteSri.Leer(contenidoXml);
        if (comprobante.Tipo == TipoComprobanteSri.NoValido)
        {
            return new(null, comprobante.Tipo, comprobante.Error ?? "Comprobante no válido");
        }
        if (comprobante.Tipo != TipoComprobanteSri.Factura)
        {
            return new(null, comprobante.Tipo,
                "Se ha reconocido un comprobante electrónico de tipo : " + Nombre(comprobante.Tipo));
        }
        try
        {
            return new(Armar(comprobante.Raiz!), TipoComprobanteSri.Factura, null);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or NullReferenceException)
        {
            return new(null, TipoComprobanteSri.Factura, "No se pudo leer la factura: " + ex.Message);
        }
    }

    private static FacturaRecibida Armar(System.Xml.Linq.XElement xml)
    {
        var info = xml.Element("infoFactura")!;
        var trib = xml.Element("infoTributaria")!;

        var emisor = new EmisorFactura(
            Ruc: Texto(trib, "ruc"),
            RazonSocial: Texto(trib, "razonSocial"),
            NombreComercial: Texto(trib, "nombreComercial"),
            DireccionMatriz: Texto(trib, "dirMatriz"),
            DireccionEstablecimiento: Texto(info, "dirEstablecimiento"),
            ContribuyenteEspecial: TextoONulo(info, "contribuyenteEspecial"),
            ObligadoContabilidad: Texto(info, "obligadoContabilidad") == "SI");

        var comprador = new CompradorFactura(
            Identificacion: Texto(info, "identificacionComprador"),
            TipoIdentificacion: Texto(info, "tipoIdentificacionComprador"),
            RazonSocial: Texto(info, "razonSocialComprador"),
            Direccion: TextoONulo(info, "direccionComprador"));

        var detalles = new List<DetalleFactura>();
        foreach (var d in xml.Element("detalles")!.Elements("detalle"))
        {
            var cantidad = Numero(d, "cantidad");
            var precioUnitario = Numero(d, "precioUnitario");
            var totalSinImpuesto = Numero(d, "precioTotalSinImpuesto");
            if (cantidad == 0)
            {
                cantidad = 1;
                precioUnitario = totalSinImpuesto;
            }
            var impuestos = (d.Element("impuestos")?.Elements("impuesto") ?? [])
                .Select(i => new ImpuestoDetalle(
                    Texto(i, "codigo"), Texto(i, "codigoPorcentaje"),
                    Numero(i, "tarifa"), Numero(i, "baseImponible"), Numero(i, "valor")))
                .ToList();
            detalles.Add(new DetalleFactura(
                Texto(d, "codigoPrincipal"), Texto(d, "codigoAuxiliar"), Texto(d, "descripcion"),
                cantidad, precioUnitario, Numero(d, "descuento"), totalSinImpuesto, impuestos));
        }

        var propina = Numero(info, "propina");
        // Corrección C1: el `.exe` sumaba solo el primer <rubro> de cada <otrosRubrosTerceros>; se suman todos
        // (si no, el total de la compra no cuadra con el de la factura).
        propina += xml.Elements("otrosRubrosTerceros").Elements("rubro").Sum(r => Numero(r, "total"));

        var impuestosTotales = new List<ImpuestoTotal>();
        foreach (var t in info.Element("totalConImpuestos")?.Elements("totalImpuesto") ?? [])
        {
            var valor = Numero(t, "valor");
            if (valor <= 0) continue;
            var codigo = Texto(t, "codigo");
            var codigoPorcentaje = Texto(t, "codigoPorcentaje");
            var tarifa = detalles.SelectMany(x => x.Impuestos)
                .FirstOrDefault(x => x.Codigo == codigo && x.CodigoPorcentaje == codigoPorcentaje)?.Tarifa;
            impuestosTotales.Add(new ImpuestoTotal(codigo, codigoPorcentaje, Numero(t, "baseImponible"), valor, tarifa));
        }

        List<PagoFactura>? pagos = null;
        if (info.Element("pagos") is { } nodoPagos)
        {
            pagos = nodoPagos.Elements("pago").Select(p => new PagoFactura(Texto(p, "formaPago"), Numero(p, "total"))).ToList();
        }

        return new FacturaRecibida(
            Ambiente: int.Parse(Texto(trib, "ambiente"), CultureInfo.InvariantCulture),
            ClaveAcceso: Texto(trib, "claveAcceso"),
            Establecimiento: Texto(trib, "estab"),
            PuntoEmision: Texto(trib, "ptoEmi"),
            Secuencial: Texto(trib, "secuencial"),
            FechaEmision: DateTime.ParseExact(Texto(info, "fechaEmision"), "dd/MM/yyyy", CultureInfo.InvariantCulture),
            Emisor: emisor,
            Comprador: comprador,
            Detalles: detalles,
            Totales: new TotalesFactura(
                Numero(info, "totalSinImpuestos"), Numero(info, "totalDescuento"), propina,
                Numero(info, "importeTotal"), impuestosTotales),
            Pagos: pagos);
    }
}
