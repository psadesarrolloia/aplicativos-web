using PsaWeb.Datil.Model;

namespace PsaWeb.Comprobantes.Venta;

/// <summary>Helpers compartidos por <see cref="ConstructorFactura"/> y <c>ConstructorNotaCredito</c>.</summary>
internal static class HelpersVenta
{
    /// <summary>Zona horaria de Ecuador (UTC-5, sin horario de verano).</summary>
    public static readonly TimeSpan ZonaEcuador = TimeSpan.FromHours(-5);

    // TarifaPorcentaje / SecuencialNumerico / AgruparImpuestos: en ConstructorFactura.
    public static double TarifaPorcentaje(double p) => ConstructorFactura.TarifaPorcentaje(p);

    public static string SecuencialNumerico(string s) => ConstructorFactura.SecuencialNumerico(s);

    public static List<Impuesto> AgruparImpuestos(IReadOnlyList<FacturaVentaLinea> lineas, string codigoIva) =>
        ConstructorFactura.AgruparImpuestos(lineas, codigoIva);

    /// <summary>Redirige el email a la casilla de pruebas cuando el ambiente es 1.</summary>
    public static string EmailDestino(string emailCliente, short ambiente, string? emailPruebas) =>
        ambiente == 1 && !string.IsNullOrEmpty(emailPruebas) && emailPruebas!.Length > 1
            ? emailPruebas
            : emailCliente;

    public static List<ItemComprobante> Items(IReadOnlyList<FacturaVentaLinea> lineas, string codigoIva) =>
        lineas.Select(l => new ItemComprobante
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
                    Codigo = codigoIva,
                    CodigoPorcentaje = l.CodigoPorcentajeIva,
                    BaseImponible = l.BaseImponibleIva,
                    Valor = l.IvaValor,
                    Tarifa = TarifaPorcentaje(l.IvaPorcentaje),
                },
            },
        }).ToList();

    public static Emisor Emisor(Retenciones.EmpresaEmisora emisor, Establecimiento establecimiento) => new()
    {
        Ruc = emisor.Ruc,
        RazonSocial = emisor.RazonSocial,
        NombreComercial = emisor.NombreComercial,
        Direccion = emisor.Direccion,
        ContribuyenteEspecial = emisor.ContribuyenteEspecial,
        ObligadoContabilidad = emisor.ObligadoContabilidad,
        Establecimiento = establecimiento,
    };

    public static Comprador Comprador(Clientes.ClienteSri cliente, string email) => new()
    {
        RazonSocial = cliente.RazonSocial,
        Identificacion = cliente.Identificacion,
        TipoIdentificacion = cliente.TipoIdentificacion,
        Email = email,
        Direccion = cliente.Direccion,
        Telefono = cliente.Telefono,
    };

    public static PersonaParaGuardar Persona(Clientes.ClienteSri cliente) => new(
        cliente.Identificacion, cliente.TipoIdentificacion, cliente.RazonSocial,
        cliente.Direccion, cliente.Email, cliente.Telefono, cliente.Fax);

    public static List<LineaFacturaParaGuardar> LineasParaGuardar(
        IReadOnlyList<FacturaVentaLinea> lineas, string codigoIva) =>
        lineas.Select(l => new LineaFacturaParaGuardar(
            l.Descripcion,
            string.IsNullOrEmpty(l.CodigoPrincipal) ? null : l.CodigoPrincipal,
            string.IsNullOrEmpty(l.CodigoAuxiliar) ? null : l.CodigoAuxiliar,
            l.Cantidad, l.PrecioUnitario, l.Descuento, l.SubtotalSinImpuestos,
            codigoIva, l.CodigoPorcentajeIva, l.BaseImponibleIva, l.IvaValor, l.IvaPorcentaje)).ToList();
}
