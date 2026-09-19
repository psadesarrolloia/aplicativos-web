using PsaWeb.Comprobantes.Compras;
using PsaWeb.Conciliacion.Data;

namespace PsaWeb.Conciliacion.Tests;

public class ClaveRevisionTests
{
    private const string Clave = "0109202601179111111100120010010000000011234567811";

    private static ComprobanteSriGuardado Sri() => new(
        Id: 1, ClaveAcceso: Clave, RucEmisor: "1791111111001", RazonSocialEmisor: "PROV",
        TipoComprobante: "Factura", SerieComprobante: "001-001-000000001",
        FechaAutorizacion: new DateTime(2026, 9, 1), FechaEmision: new DateOnly(2026, 9, 1),
        Subtotal: 100, Iva: 12, Total: 112, NumeroDocumentoModificado: null, Estado: null, FechaVerificacionEstado: null);

    private static CompraSage Sage(long postOrder = 77) => new(
        PostOrder: postOrder, RucProveedor: "1791111111001", NombreProveedor: "PROV", Referencia: "001-001-000000001",
        Fecha: new DateOnly(2026, 9, 1), Autorizacion: Clave, Subtotal: 100, Iva: 12, Total: 112);

    private static FilaConciliacion Fila(ClasificacionConciliacion c, string[] dif, bool conSri = true, bool conSage = true) =>
        new(conSri ? Clave : null, conSri ? Sri() : null, conSage ? Sage() : null, c, dif);

    [Fact]
    public void Con_comprobante_del_SRI_la_clave_es_la_clave_de_acceso()
    {
        Assert.Equal($"C:{Clave}", ClaveRevision.De(Fila(ClasificacionConciliacion.ValoresDistintos, ["x"])));
    }

    [Fact]
    public void Sin_comprobante_del_SRI_la_clave_es_el_PostOrder_de_Sage()
    {
        var fila = Fila(ClasificacionConciliacion.SoloEnSage, [], conSri: false);

        Assert.Equal("P:77", ClaveRevision.De(fila));
    }

    [Fact]
    public void Sin_revision_la_fila_queda_sin_revisar()
    {
        Assert.Equal(EstadoRevision.SinRevisar, ClaveRevision.Evaluar(Fila(ClasificacionConciliacion.ValoresDistintos, ["x"]), null));
    }

    [Fact]
    public void Revision_con_la_misma_huella_esta_revisada()
    {
        var fila = Fila(ClasificacionConciliacion.ValoresDistintos, ["Total: SRI 1.00 vs Sage 2.00"]);
        var revision = new RevisionConciliacion { Huella = ClaveRevision.Huella(fila) };

        Assert.Equal(EstadoRevision.Revisada, ClaveRevision.Evaluar(fila, revision));
    }

    [Fact]
    public void Si_cambia_la_diferencia_la_revision_queda_desactualizada()
    {
        var revisada = Fila(ClasificacionConciliacion.ValoresDistintos, ["Total: SRI 1.00 vs Sage 2.00"]);
        var ahora = Fila(ClasificacionConciliacion.ValoresDistintos, ["Total: SRI 1.00 vs Sage 3.00"]);
        var revision = new RevisionConciliacion { Huella = ClaveRevision.Huella(revisada) };

        Assert.Equal(EstadoRevision.Desactualizada, ClaveRevision.Evaluar(ahora, revision));
    }

    [Fact]
    public void Si_cambia_la_categoria_la_revision_queda_desactualizada()
    {
        var revisada = Fila(ClasificacionConciliacion.SoloEnSri, [], conSage: false);
        var ahora = Fila(ClasificacionConciliacion.MetadataDistinta, ["Fecha: x"]);
        var revision = new RevisionConciliacion { Huella = ClaveRevision.Huella(revisada) };

        Assert.Equal(EstadoRevision.Desactualizada, ClaveRevision.Evaluar(ahora, revision));
    }

    [Fact]
    public void La_revision_heredada_sin_huella_aplica_siempre()
    {
        var fila = Fila(ClasificacionConciliacion.ValoresDistintos, ["Total: SRI 1.00 vs Sage 2.00"]);

        Assert.Equal(EstadoRevision.Revisada, ClaveRevision.Evaluar(fila, new RevisionConciliacion { Huella = "" }));
    }
}
