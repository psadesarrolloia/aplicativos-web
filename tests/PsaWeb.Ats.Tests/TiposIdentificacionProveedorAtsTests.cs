using PsaWeb.Ats.Compras;

namespace PsaWeb.Ats.Tests;

public class TiposIdentificacionProveedorAtsTests
{
    [Theory]
    [InlineData("1790011110001", TiposIdentificacionProveedorAts.Ruc)]
    [InlineData("0102030405", TiposIdentificacionProveedorAts.Cedula)]
    [InlineData("PA1234567", TiposIdentificacionProveedorAts.Exterior)]
    [InlineData("", TiposIdentificacionProveedorAts.Exterior)]
    public void Deducir_clasifica_por_longitud(string identificacion, string esperado)
    {
        Assert.Equal(esperado, TiposIdentificacionProveedorAts.Deducir(identificacion));
    }
}
