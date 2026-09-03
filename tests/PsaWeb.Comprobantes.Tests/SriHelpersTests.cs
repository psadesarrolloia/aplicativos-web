using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Comprobantes.Tests;

public class TiposIdentificacionTests
{
    [Theory]
    [InlineData("1712345678", "05")]           // cédula: 10 dígitos
    [InlineData("1790352897001", "04")]        // RUC: 13 dígitos
    [InlineData("9999999999999", "07")]        // consumidor final
    [InlineData("AB123456", "08")]             // pasaporte / exterior
    [InlineData("", "08")]
    [InlineData("123", "08")]
    public void Deduce_el_tipo_por_la_identificacion(string id, string esperado)
    {
        Assert.Equal(esperado, TiposIdentificacion.Deducir(id));
    }
}

public class EmailSriTests
{
    [Theory]
    [InlineData("prov@ejemplo.com", true)]
    [InlineData("nombre.apellido+etiqueta@sub.dominio.ec", true)]
    [InlineData("sin-arroba.com", false)]
    [InlineData("espacio @ejemplo.com", false)]
    [InlineData("prov@ejemplo.com texto", false)]
    [InlineData("", false)]
    public void Valida_igual_que_el_original(string email, bool esperado)
    {
        Assert.Equal(esperado, EmailSri.EsValido(email));
    }
}
