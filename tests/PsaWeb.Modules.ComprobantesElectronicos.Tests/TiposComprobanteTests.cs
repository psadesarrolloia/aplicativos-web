using PsaWeb.Seguridad;

namespace PsaWeb.Modules.ComprobantesElectronicos.Tests;

/// <summary>Los 4 comprobantes comparten módulo: lo que los distingue vive en un solo lugar (<see cref="Tipos"/>).</summary>
public class TiposComprobanteTests
{
    [Fact]
    public void Hay_exactamente_cuatro_tipos_con_codigos_SRI_y_rutas_unicos()
    {
        Assert.Equal(4, Tipos.Todos.Count);
        Assert.Equal(new[] { "01", "03", "04", "07" }, Tipos.Todos.Select(t => t.CodDoc).OrderBy(c => c));
        Assert.Equal(4, Tipos.Todos.Select(t => t.Ruta).Distinct().Count());
        Assert.All(Enum.GetValues<TipoComprobante>(), t => Assert.Equal(t, Tipos.De(t).Tipo));
    }

    [Fact]
    public void Solo_la_retencion_es_de_compra_sin_iva_y_las_de_compra_hablan_de_proveedor()
    {
        Assert.True(Tipos.De(TipoComprobante.Retencion).EsRetencion);
        Assert.Equal(1, Tipos.Todos.Count(t => t.EsRetencion));
        Assert.Equal("Proveedor", Tipos.De(TipoComprobante.Retencion).PersonaEtiqueta);
        Assert.Equal("Proveedor", Tipos.De(TipoComprobante.Liquidacion).PersonaEtiqueta);
        Assert.Equal("Cliente", Tipos.De(TipoComprobante.Factura).PersonaEtiqueta);
        Assert.Equal("Cliente", Tipos.De(TipoComprobante.NotaCredito).PersonaEtiqueta);
    }

    [Fact]
    public void PorCodDoc_resuelve_cada_tipo()
    {
        Assert.Equal(TipoComprobante.Factura, Tipos.PorCodDoc("01").Tipo);
        Assert.Equal(TipoComprobante.Retencion, Tipos.PorCodDoc("07").Tipo);
        Assert.Equal(TipoComprobante.NotaCredito, Tipos.PorCodDoc("04 ").Tipo);
        Assert.Equal(TipoComprobante.Liquidacion, Tipos.PorCodDoc("03").Tipo);
    }

    [Theory]
    [InlineData(TipoComprobante.Factura, "fe-facturas")]
    [InlineData(TipoComprobante.Retencion, "retenciones")]
    [InlineData(TipoComprobante.NotaCredito, "fe-notas-credito")]
    [InlineData(TipoComprobante.Liquidacion, "fe-liquidaciones")]
    public void El_catalogo_del_menu_coincide_con_la_ruta_y_las_llaves_de_cada_tipo(TipoComprobante tipo, string appId)
    {
        var info = Tipos.De(tipo);
        var app = AppCatalogo.Todas.Single(a => a.Id == appId);

        Assert.Equal(info.Ruta, app.Ruta);
        Assert.Equal(
            new[] { info.PermisoVer, info.PermisoHacer, info.PermisoLote }.OrderBy(x => x),
            app.PermisosQueLaHabilitan.OrderBy(x => x));
    }

    [Fact]
    public void Las_llaves_de_todos_los_tipos_son_distintas_entre_si()
    {
        var llaves = Tipos.Todos
            .SelectMany(t => new[] { t.PermisoVer, t.PermisoHacer, t.PermisoLote, t.PermisoAnular })
            .ToList();
        Assert.Equal(16, llaves.Distinct().Count());
    }
}
