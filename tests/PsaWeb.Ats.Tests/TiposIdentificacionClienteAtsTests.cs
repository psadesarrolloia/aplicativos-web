using PsaWeb.Ats.Ventas;

namespace PsaWeb.Ats.Tests;

public class TiposIdentificacionClienteAtsTests
{
    [Theory]
    [InlineData("1790011110001", TiposIdentificacionClienteAts.Ruc)] // 13 dígitos
    [InlineData("0102030405", TiposIdentificacionClienteAts.Cedula)] // 10 dígitos
    [InlineData("9999999999999", TiposIdentificacionClienteAts.ConsumidorFinal)] // caso especial, antes que el largo
    [InlineData("PA1234567", TiposIdentificacionClienteAts.Exterior)] // no numérico
    [InlineData("020550511941", TiposIdentificacionClienteAts.Exterior)] // 12 dígitos: ni 10 ni 13
    [InlineData("", TiposIdentificacionClienteAts.Exterior)]
    public void Deducir_clasifica_segun_longitud_y_contenido(string identificacion, string esperado)
    {
        Assert.Equal(esperado, TiposIdentificacionClienteAts.Deducir(identificacion));
    }
}
