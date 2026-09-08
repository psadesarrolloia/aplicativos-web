using PsaWeb.Comprobantes.Clientes;
using PsaWeb.Datil.Model;

namespace PsaWeb.Comprobantes.Venta.Muestra;

/// <summary>
/// Datos de muestra de facturas de venta para desarrollar el módulo sin tocar
/// Sage 50 (PREDATOR no siempre llega a la empresa multi-compañía). Equivale al
/// repo de muestra que se usó en el piloto.
/// </summary>
public static class FacturasVentaMuestra
{
    public static IReadOnlyList<FacturaPendiente> Pendientes() => new[]
    {
        new FacturaPendiente("9001", "001-003-000013538", new DateTime(2026, 9, 4)),
        new FacturaPendiente("9002", "001-003-000013539", new DateTime(2026, 9, 4)),
        new FacturaPendiente("9003", "001-001-000000777", new DateTime(2026, 9, 3)),
    };

    public static FacturaVentaLeida? Detalle(string postOrder) => postOrder switch
    {
        "9001" => FacturaSimpleConIva(),
        "9002" => FacturaAExterior(),
        "9003" => FacturaConDescuento(),
        _ => null,
    };

    private static ClienteSri Cliente(
        string id = "1790011110001", string tipo = "04", string nombre = "CLIENTE DEMO S.A.",
        string email = "demo@ejemplo.com") => new()
    {
        Identificacion = id,
        TipoIdentificacion = tipo,
        RazonSocial = nombre,
        Direccion = "Av. Demo 100 y Prueba",
        Email = email,
        Telefono = "022000000",
        Errores = Array.Empty<string>(),
    };

    private static FacturaVentaLeida FacturaSimpleConIva()
    {
        var linea = new FacturaVentaLinea
        {
            Descripcion = "Servicio de mantenimiento",
            Cantidad = 1, PrecioUnitario = 250, SubtotalSinImpuestos = 250,
            BaseImponibleIva = 250, IvaValor = 30, IvaPorcentaje = 0.12,
            CodigoPorcentajeIva = "2", CodigoPrincipal = "SERV-01",
        };
        return Armar("9001", "001-003-000013538", new DateTime(2026, 9, 4), null, Cliente(), linea,
            totalConImpuestos: 280, iva: 30, sinImpuestos: 250, baseIva: 250);
    }

    private static FacturaVentaLeida FacturaAExterior()
    {
        var linea = new FacturaVentaLinea
        {
            Descripcion = "Consultoría", Cantidad = 1, PrecioUnitario = 500,
            SubtotalSinImpuestos = 500, BaseImponibleIva = 500, IvaValor = 0,
            IvaPorcentaje = 0, CodigoPorcentajeIva = "0", CodigoPrincipal = "CONS-01",
        };
        return Armar("9002", "001-003-000013539", new DateTime(2026, 9, 4), null,
            Cliente(id: "X1234567", tipo: "06", nombre: "JOHN DOE", email: "john@abroad.example"),
            linea, totalConImpuestos: 500, iva: 0, sinImpuestos: 500, baseIva: 0);
    }

    private static FacturaVentaLeida FacturaConDescuento()
    {
        var linea = new FacturaVentaLinea
        {
            Descripcion = "Equipo", Cantidad = 2, PrecioUnitario = 45,
            SubtotalSinImpuestos = 90, BaseImponibleIva = 90, IvaValor = 10.8,
            IvaPorcentaje = 0.12, CodigoPorcentajeIva = "2", CodigoPrincipal = "EQ-1", Descuento = 10,
        };
        return Armar("9003", "001-001-000000777", new DateTime(2026, 9, 3), new DateTime(2026, 10, 3),
            Cliente(), linea, totalConImpuestos: 100.8, iva: 10.8, sinImpuestos: 90, baseIva: 90,
            descuentoTotal: 10);
    }

    private static FacturaVentaLeida Armar(
        string postOrder, string numero, DateTime fecha, DateTime? vence, ClienteSri cliente,
        FacturaVentaLinea linea, double totalConImpuestos, double iva, double sinImpuestos,
        double baseIva, double descuentoTotal = 0)
    {
        var num = Sri.NumeroDocumentoSri.AnalizarEstricto(numero);
        return new FacturaVentaLeida
        {
            PostOrderPeach = postOrder,
            Cliente = cliente,
            Lineas = new[] { linea },
            InfoAdicional = Array.Empty<InfoAdicionalItem>(),
            Cabecera = new FacturaVentaCabecera
            {
                NumeroCompleto = numero,
                Secuencial = num.Secuencial,
                CodigoEstablecimiento = num.CodigoEstablecimiento,
                PuntoEmision = num.PuntoEmision,
                CustomerRecordNumber = "1",
                FechaEmision = fecha,
                FechaVencimiento = vence,
                TotalConImpuestos = totalConImpuestos,
                IvaValor = iva,
                TotalSinImpuestos = sinImpuestos,
                BaseImponibleIva = baseIva,
                DescuentoTotal = descuentoTotal,
                CodigoPorcentajeIva = linea.CodigoPorcentajeIva,
                CodigoIva = "2",
            },
        };
    }
}
