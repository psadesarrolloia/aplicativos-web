using PsaWeb.Comprobantes.Clientes;
using static PsaWeb.Comprobantes.Clientes.LectorCliente;

namespace PsaWeb.Comprobantes.Tests;

public class LectorClienteTests
{
    private static ClienteSri Mapear(
        string pasaporte = "", string ruc = "1790011110001", string ruc2 = "",
        string nombre = "CLIENTE ", string dir1 = "Av. 6 de Diciembre", string dir2 = "y Patria",
        string email = "cliente@ejemplo.com", string tel = "022345678", string fax = "",
        DireccionSage[]? direcciones = null)
        => LectorCliente.Mapear(pasaporte, ruc, ruc2, nombre, dir1, dir2, email, tel, fax,
            direcciones ?? Array.Empty<DireccionSage>());

    [Fact]
    public void Cliente_con_RUC_valido()
    {
        var c = Mapear();

        Assert.True(c.Ok);
        Assert.Equal("1790011110001", c.Identificacion);
        Assert.Equal("04", c.TipoIdentificacion);
        Assert.Equal("CLIENTE", c.RazonSocial);
        Assert.Equal("Av. 6 de Diciembre y Patria", c.Direccion);
        Assert.Equal("022345678", c.Telefono);
        Assert.Null(c.Fax);
    }

    [Fact]
    public void Cedula_de_10_digitos_es_tipo_05()
    {
        var c = Mapear(ruc: "1712345678");
        Assert.Equal("05", c.TipoIdentificacion);
    }

    [Fact]
    public void Pasaporte_en_CustomField5_gana_y_es_tipo_06()
    {
        var c = Mapear(pasaporte: "A1234567", ruc: "1790011110001");
        Assert.Equal("A1234567", c.Identificacion);
        Assert.Equal("06", c.TipoIdentificacion);
    }

    [Fact]
    public void Sin_ruc_toma_el_country_de_la_direccion_principal()
    {
        var c = Mapear(ruc: "", ruc2: "0999999999001");
        Assert.Equal("0999999999001", c.Identificacion);
    }

    [Fact]
    public void Sin_ruc_ni_country_principal_usa_la_direccion_con_country_cuando_hay_varias()
    {
        var direcciones = new[]
        {
            new DireccionSage("", "Bodega", ""),
            new DireccionSage("1790011110001", "Matriz", "Piso 2"),
        };
        var c = Mapear(ruc: "", ruc2: "", dir1: "", dir2: "", direcciones: direcciones);

        Assert.Equal("1790011110001", c.Identificacion);
        Assert.Equal("04", c.TipoIdentificacion);
        Assert.Equal("Matriz Piso 2", c.Direccion);
    }

    [Fact]
    public void Email_invalido_se_reporta()
    {
        var c = Mapear(email: "ok@ejemplo.com,malo,otro@ok.ec");
        Assert.Contains(c.Errores, e => e.Contains("malo"));
        Assert.DoesNotContain(c.Errores, e => e.Contains("ok@ejemplo.com"));
    }

    [Fact]
    public void Sin_email_es_error()
    {
        var c = Mapear(email: "");
        Assert.Contains(c.Errores, e => e.Contains("no tiene email"));
    }

    [Fact]
    public void Sin_identificacion_es_error()
    {
        var c = Mapear(ruc: "", ruc2: "");
        Assert.Contains(c.Errores, e => e.Contains("identificación del cliente"));
    }

    [Fact]
    public void Fax_de_mas_de_un_caracter_se_conserva()
    {
        Assert.Equal("022999888", Mapear(fax: "022999888").Fax);
        Assert.Null(Mapear(fax: "0").Fax);
    }
}
