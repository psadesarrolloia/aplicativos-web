using PsaWeb.Compras.Catalogo;
using PsaWeb.Compras.Sri;

namespace PsaWeb.Compras.Tests;

public class ReglasProveedorTests
{
    [Theory]
    [InlineData("Compañía Ñandú S.A.", "COMPANIA NANDU S.A.")]
    [InlineData("  Distribuidora*  Pérez + Hijos?  ", "DISTRIBUIDORA PEREZ")]
    [InlineData("CORPORACION LA FAVORITA C.A.", "CORPORACION LA FAVOR")]
    public void Id_propuesto_limpio_y_recortado(string razon, string esperado) =>
        Assert.Equal(esperado, ReglasProveedor.IdPropuesto(razon, []));

    [Fact]
    public void Id_propuesto_agrega_sufijo_si_ya_existe()
    {
        Assert.Equal("CORPORACION LA FAV-2", ReglasProveedor.IdPropuesto("CORPORACION LA FAVORITA C.A.", ["corporacion la favor"]));
        Assert.Equal("CORPORACION LA FAV-3", ReglasProveedor.IdPropuesto("CORPORACION LA FAVORITA C.A.", ["CORPORACION LA FAVOR", "CORPORACION LA FAV-2"]));
    }

    [Theory]
    [InlineData("1799999999001", "04", "")]
    [InlineData("1799999999", "05", "")]
    [InlineData("1799999999002", "06", "1799999999002")] // 13 dígitos sin 001: el `.exe` lo trata como pasaporte
    [InlineData("AB123456", "06", "AB123456")]
    public void Identificacion_de_proveedor_nuevo(string valor, string tipo, string cf4)
    {
        var (t, pais, c) = ReglasProveedor.Identificacion(valor);
        Assert.Equal((tipo, valor, cf4), (t, pais, c));
    }

    [Fact]
    public void Nombre_y_direccion_se_parten_como_el_exe()
    {
        Assert.Equal(("NOMBRE CORTO", ""), ReglasProveedor.PartirNombre("NOMBRE CORTO"));
        var largo = new string('A', 30) + new string('B', 40);
        Assert.Equal((new string('A', 30), new string('B', 30)), ReglasProveedor.PartirNombre(largo));
        Assert.Equal((new string('A', 30), "BB"), ReglasProveedor.PartirDireccion(new string('A', 30) + "BB"));
    }

    [Fact]
    public void Telefono2_guarda_obligado_y_contribuyente_especial_completo()
    {
        Assert.Equal("OC-CE#5368#", ReglasProveedor.Telefono2(true, "5368")); // Corrección C5: el `.exe` dejaba «536»
        Assert.Equal("SC-", ReglasProveedor.Telefono2(false, null));
    }

    [Fact]
    public void Nuevo_desde_factura()
    {
        var f = LectorFacturaSri.Leer(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "factura-sintetica.xml"))).Factura!;

        var p = ReglasProveedor.NuevoDesdeFactura(f, "PROVEEDOR DE PRUEBA", "60505", "a@b.test");

        Assert.Equal(("04", "1799999999001", "PROVEEDOR DE PRUEBA S.A.", "AV. SIEMPRE VIVA 123", "OC-CE#5368#"),
            (p.TipoIdentificacion, p.Identificacion, p.NombreCompleto, p.Direccion1, p.Telefono2));
    }
}
