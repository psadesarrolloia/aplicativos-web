using PsaWeb.Ats.Compras;

namespace PsaWeb.Ats.Tests;

public class LectorProveedorAtsTests
{
    private static readonly CodigoIdentificacionCompras[] CatalogoReal =
    {
        new("04", "01"),
        new("05", "02"),
        new("06", "03"),
        new("08", "03"),
    };

    [Fact]
    public void Resuelve_por_catalogo_cuando_hay_una_sola_coincidencia()
    {
        // Caso real de CPTDC: OurAccountWithThem=04 -> tpIdProv=01 (RUC),
        // identificacion viene de Address.Country.
        var fila = new LectorProveedorAts.FilaProveedorCruda("04", Pasaporte: null, Ruc: null, RucAlterno: "1793198281001");

        var r = LectorProveedorAts.ResolverIdentificacion(fila, CatalogoReal);

        Assert.Equal("01", r.TipoIdentificacion);
        Assert.Equal("1793198281001", r.Identificacion);
        Assert.Null(r.Advertencia);
        Assert.True(r.ViaCatalogo);
    }

    [Fact]
    public void Catalogo_con_RucAlterno_nulo_deja_la_identificacion_vacia()
    {
        var fila = new LectorProveedorAts.FilaProveedorCruda("06", Pasaporte: null, Ruc: null, RucAlterno: null);

        var r = LectorProveedorAts.ResolverIdentificacion(fila, CatalogoReal);

        Assert.Equal("03", r.TipoIdentificacion);
        Assert.Equal(string.Empty, r.Identificacion);
    }

    [Fact]
    public void Sin_coincidencia_en_catalogo_cae_en_pasaporte()
    {
        var fila = new LectorProveedorAts.FilaProveedorCruda("99", Pasaporte: "PE10915140", Ruc: null, RucAlterno: null);

        var r = LectorProveedorAts.ResolverIdentificacion(fila, CatalogoReal);

        Assert.Equal(TiposIdentificacionProveedorAts.Exterior, r.TipoIdentificacion);
        Assert.Equal("PE10915140", r.Identificacion);
        Assert.NotNull(r.Advertencia);
        Assert.False(r.ViaCatalogo);
    }

    [Fact]
    public void Pasaporte_vacio_cae_en_RUC_y_deduce_el_tipo()
    {
        var fila = new LectorProveedorAts.FilaProveedorCruda("99", Pasaporte: "", Ruc: "0993406484001", RucAlterno: null);

        var r = LectorProveedorAts.ResolverIdentificacion(fila, CatalogoReal);

        Assert.Equal(TiposIdentificacionProveedorAts.Ruc, r.TipoIdentificacion); // 13 dígitos
        Assert.Equal("0993406484001", r.Identificacion);
        Assert.False(r.ViaCatalogo);
    }

    [Fact]
    public void Pasaporte_y_RUC_vacios_cae_en_RucAlterno()
    {
        var fila = new LectorProveedorAts.FilaProveedorCruda("99", Pasaporte: "", Ruc: "", RucAlterno: "20550511941");

        var r = LectorProveedorAts.ResolverIdentificacion(fila, CatalogoReal);

        Assert.Equal(TiposIdentificacionProveedorAts.Exterior, r.TipoIdentificacion); // 11 dígitos, ni 10 ni 13
        Assert.Equal("20550511941", r.Identificacion);
    }

    [Fact]
    public void Sin_pasaporte_ni_RUC_ni_alterno_no_resuelve_tipo()
    {
        var fila = new LectorProveedorAts.FilaProveedorCruda("99", Pasaporte: null, Ruc: null, RucAlterno: null);

        var r = LectorProveedorAts.ResolverIdentificacion(fila, CatalogoReal);

        Assert.Null(r.TipoIdentificacion);
        Assert.Equal(string.Empty, r.Identificacion);
    }
}
