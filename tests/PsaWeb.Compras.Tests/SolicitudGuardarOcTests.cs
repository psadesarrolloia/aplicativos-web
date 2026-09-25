using System.Text.Json;
using PsaWeb.Compras.Armado;
using PsaWeb.Compras.Bridge;
using PsaWeb.Compras.Catalogo;
using PsaWeb.SageBridge.Contratos;

namespace PsaWeb.Compras.Tests;

public class SolicitudGuardarOcTests
{
    private static OcArmada Oc(bool retencion = true) => new(
        "OC-8757", "PROVEEDOR PRUEBA", "PROVEEDOR PRUEBA", "DIR 1", "", "1799999999001",
        new DateTime(2026, 9, 11), new DateTime(2026, 9, 12), "FACTURA", "001-002-000000123",
        retencion ? "001-001-000025829" : null, retencion, "01", null, "20000",
        [
            new LineaOc(TipoLineaOc.Detalle, "C3", "CON IVA", 4.627m, 13.04m / 4.627m, 13.04m, "60505", null),
            new LineaOc(TipoLineaOc.Relleno, null, ".", 0, 0, 0, null, null),
        ]);

    [Fact]
    public void Arma_el_payload_con_numeracion_automatica_y_proveedor_existente()
    {
        var s = SolicitudGuardarOc.Crear(Oc(), CatalogoPrueba.Proveedor(), numeroOcAutomatico: true, numeroRetencionAutomatico: true);

        Assert.Equal(TiposTrabajo.GuardarOc, s.Tipo);
        var p = JsonSerializer.Deserialize<PayloadGuardarOc>(s.PayloadJson)!;
        Assert.Equal((null, null, "OC", true), (p.NumeroOc, p.NumeroRetencion, p.PrefijoOc, p.RequiereRetencion));
        Assert.Equal(("2026-09-11", "2026-09-12", "001-002-000000123", "FACTURA", "01"), (p.Fecha, p.FechaRegistro, p.NumeroFactura, p.ShipVia, p.EstadoSustento));
        Assert.Equal(["Detalle", "Relleno"], p.Lineas.Select(x => x.Tipo));
        Assert.Equal(13.04m / 4.627m, p.Lineas[0].PrecioUnitario);
        Assert.False(p.Proveedor.EsNuevo);
        Assert.Equal(("1799999999001", "04", "OC-"), (p.Proveedor.Identificacion, p.Proveedor.TipoIdentificacion, p.Proveedor.Telefono2));
        Assert.StartsWith("oc|PROVEEDOR PRUEBA|001-002-000000123|", s.ClaveIdempotencia);
    }

    [Fact]
    public void Numeros_manuales_proveedor_nuevo_y_clave_segun_contenido()
    {
        var nuevo = CatalogoPrueba.Proveedor() with { RecordNumber = null };
        var a = SolicitudGuardarOc.Crear(Oc(), nuevo, numeroOcAutomatico: false, numeroRetencionAutomatico: false);
        var b = SolicitudGuardarOc.Crear(Oc(), nuevo, false, false);
        var sinRetencion = SolicitudGuardarOc.Crear(Oc(retencion: false), nuevo, false, false);

        var p = JsonSerializer.Deserialize<PayloadGuardarOc>(a.PayloadJson)!;
        Assert.Equal(("OC-8757", "001-001-000025829", true), (p.NumeroOc, p.NumeroRetencion, p.Proveedor.EsNuevo));
        Assert.Equal(a.ClaveIdempotencia, b.ClaveIdempotencia);             // doble clic: mismo trabajo
        Assert.NotEqual(a.ClaveIdempotencia, sinRetencion.ClaveIdempotencia); // cambió el contenido: trabajo nuevo
        Assert.False(JsonSerializer.Deserialize<PayloadGuardarOc>(sinRetencion.PayloadJson)!.RequiereRetencion);
    }
}
