using PsaWeb.PeachEbills;

namespace PsaWeb.PeachEbills.Tests;

public class DbSecretTests
{
    [Fact]
    public void Descifra_un_valor_real_de_la_base()
    {
        // Valor tomado de PeachConnString.pwd (RUC 1791741951001).
        Assert.Equal("JCV1234", DbSecret.Decrypt("4gOp/yqYXe4="));
    }

    [Theory]
    [InlineData("JCV1234")]
    [InlineData("clave con espacios y ñ")]
    [InlineData("")]
    public void Encrypt_y_Decrypt_son_inversos(string original)
    {
        Assert.Equal(original, DbSecret.Decrypt(DbSecret.Encrypt(original)));
    }

    [Fact]
    public void Decrypt_de_null_o_vacio_devuelve_vacio()
    {
        Assert.Equal(string.Empty, DbSecret.Decrypt(null));
        Assert.Equal(string.Empty, DbSecret.Decrypt(""));
    }
}
