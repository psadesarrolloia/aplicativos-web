using PsaWeb.Comprobantes.Proveedores;

namespace PsaWeb.Comprobantes.Tests;

public class LectorProveedorTests
{
    private static ProveedorSri Mapear(
        string id = "1790352897001", string tipoId = "04", string nombre = "PROVEEDOR CIA LTDA",
        string nombreExtra = "", string dir1 = "Av. Amazonas 123", string dir2 = "Piso 4",
        string email = "prov@ejemplo.com", string tel = "022345678")
        => LectorProveedor.Mapear(id, tipoId, nombre, nombreExtra, dir1, dir2, email, tel);

    [Fact]
    public void Proveedor_valido_no_tiene_errores()
    {
        var p = Mapear();

        Assert.True(p.Ok);
        Assert.Equal("1790352897001", p.Identificacion);
        Assert.Equal("04", p.TipoIdentificacion);
        Assert.Equal("PROVEEDOR CIA LTDA", p.RazonSocial);
        Assert.Equal("Av. Amazonas 123 Piso 4", p.Direccion);
        Assert.Equal("022345678", p.Telefono);
    }

    [Fact]
    public void Concatena_nombre_y_customfield0()
    {
        var p = Mapear(nombre: "COMERCIAL ", nombreExtra: "DEL PACIFICO S.A.");
        Assert.Equal("COMERCIAL DEL PACIFICO S.A.", p.RazonSocial);
    }

    [Fact]
    public void Tipo_de_identificacion_que_no_coincide_es_error()
    {
        // 13 dígitos => corresponde "04", pero el proveedor tiene "05"
        var p = Mapear(id: "1790352897001", tipoId: "05");
        Assert.False(p.Ok);
        Assert.Contains(p.Errores, e => e.Contains("tipo de identificación"));
    }

    [Fact]
    public void Nombre_ANULAD_es_retencion_anulada()
    {
        var p = Mapear(nombre: "ANULADO 2026");
        Assert.Contains(p.Errores, e => e.Contains("anulada"));
    }

    [Fact]
    public void Email_invalido_se_reporta_por_cada_uno()
    {
        var p = Mapear(email: "bueno@ejemplo.com; malo; otro@ok.ec");
        Assert.Contains(p.Errores, e => e.Contains("malo"));
        Assert.DoesNotContain(p.Errores, e => e.Contains("bueno@ejemplo.com"));
    }

    [Fact]
    public void Sin_email_es_error()
    {
        var p = Mapear(email: "");
        Assert.Contains(p.Errores, e => e.Contains("no tiene email"));
    }

    [Fact]
    public void Direccion_con_una_sola_linea()
    {
        var p = Mapear(dir1: "", dir2: "Solo la segunda línea");
        Assert.Equal("Solo la segunda línea", p.Direccion);
    }

    [Fact]
    public void Identificacion_vacia_es_error()
    {
        var p = Mapear(id: "   ", tipoId: "08");
        Assert.Contains(p.Errores, e => e.Contains("vacía"));
    }
}
