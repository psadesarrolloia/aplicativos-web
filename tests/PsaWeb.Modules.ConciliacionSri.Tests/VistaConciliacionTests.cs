using PsaWeb.Comprobantes.Compras;
using PsaWeb.Conciliacion;
using PsaWeb.Conciliacion.Data;

namespace PsaWeb.Modules.ConciliacionSri.Tests;

public class VistaConciliacionTests
{
    private static ComprobanteSriGuardado Sri(
        string clave, string serie, string proveedor, decimal total, string? estado = null) => new(
        Id: 1, ClaveAcceso: clave, RucEmisor: "1791111111001", RazonSocialEmisor: proveedor,
        TipoComprobante: "Factura", SerieComprobante: serie,
        FechaAutorizacion: new DateTime(2026, 9, 1), FechaEmision: new DateOnly(2026, 9, 1),
        Subtotal: total, Iva: 0, Total: total, NumeroDocumentoModificado: null,
        Estado: estado, FechaVerificacionEstado: null);

    private static CompraSage Sage(string referencia, string proveedor, decimal total, long postOrder = 1) => new(
        PostOrder: postOrder, RucProveedor: "1791111111001", NombreProveedor: proveedor, Referencia: referencia,
        Fecha: new DateOnly(2026, 9, 1), Autorizacion: "", Subtotal: total, Iva: 0, Total: total);

    private static FilaConRevision Fila(
        ClasificacionConciliacion clasificacion, ComprobanteSriGuardado? sri, CompraSage? sage,
        string[]? diferencias = null, RevisionDeFila? revision = null) =>
        new(new FilaConciliacion(sri?.ClaveAcceso, sri, sage, clasificacion, diferencias ?? []), revision);

    private static readonly FilaConRevision Ecua = Fila(
        ClasificacionConciliacion.ValoresDistintos,
        Sri(new string('1', 49), "001-053-000139279", "ECUASANITAS S.A.", 974.32m, "Autorizado"),
        Sage("001-053-000139279", "ECUASANITAS", 970m),
        ["Subtotal: SRI 969.47 vs Sage 974.32"]);

    private static readonly FilaConRevision Setel = Fila(
        ClasificacionConciliacion.ValoresDistintos,
        Sri(new string('2', 49), "002-001-000000010", "SETEL S.A.", 35.45m, "Anulado"),
        Sage("002-001-000000010", "SETEL S.A.", 30m),
        ["Total: SRI 35.45 vs Sage 30.00"],
        new RevisionDeFila("Corresponde a ICE", "lparedes", new DateTime(2026, 9, 19), EstadoRevision.Revisada));

    private static readonly FilaConRevision Lechon = Fila(
        ClasificacionConciliacion.ValoresDistintos,
        Sri(new string('3', 49), "001-891-000000005", "LECHON BONILLA SEGUNDO", 62.08m),
        Sage("001-891-000000005", "LECHON BONILLA", 62.06m),
        ["Total: SRI 62.08 vs Sage 62.06"],
        new RevisionDeFila("Viejo", "lparedes", new DateTime(2026, 9, 1), EstadoRevision.Desactualizada));

    private static readonly FilaConRevision[] Todas = [Ecua, Setel, Lechon];

    private static string[] Documentos(IReadOnlyList<FilaConRevision> filas) => filas.Select(f => f.Documento).ToArray();

    [Fact]
    public void Sin_criterios_devuelve_todo_en_el_orden_original()
    {
        Assert.Equal(new[] { Ecua, Setel, Lechon }, VistaConciliacion.Aplicar(Todas, new CriteriosVista()));
    }

    [Fact]
    public void La_busqueda_global_mira_documento_proveedores_diferencias_y_comentario()
    {
        Assert.Equal([Ecua], VistaConciliacion.Aplicar(Todas, new CriteriosVista(Busqueda: "ecuasan")));
        Assert.Equal([Setel], VistaConciliacion.Aplicar(Todas, new CriteriosVista(Busqueda: "002-001")));
        Assert.Equal([Setel], VistaConciliacion.Aplicar(Todas, new CriteriosVista(Busqueda: "ice")));
        Assert.Equal([Ecua], VistaConciliacion.Aplicar(Todas, new CriteriosVista(Busqueda: "969.47")));
    }

    [Fact]
    public void La_clave_de_acceso_no_se_busca()
    {
        Assert.Empty(VistaConciliacion.Aplicar(Todas, new CriteriosVista(Busqueda: new string('1', 49))));
    }

    [Fact]
    public void Los_filtros_por_columna_se_combinan()
    {
        var r = VistaConciliacion.Aplicar(Todas, new CriteriosVista(ProveedorSri: "s.a.", Diferencias: "35.45"));

        Assert.Equal([Setel], r);
    }

    [Fact]
    public void Filtro_de_estado_en_SRI_distingue_sin_verificar_autorizado_y_no_vigente()
    {
        Assert.Equal([Lechon], VistaConciliacion.Aplicar(Todas, new CriteriosVista(EstadoSri: CriteriosVista.EstadoSinVerificar)));
        Assert.Equal([Ecua], VistaConciliacion.Aplicar(Todas, new CriteriosVista(EstadoSri: CriteriosVista.EstadoAutorizado)));
        Assert.Equal([Setel], VistaConciliacion.Aplicar(Todas, new CriteriosVista(EstadoSri: CriteriosVista.EstadoNoVigente)));
    }

    [Fact]
    public void Filtro_de_revision_pendientes_incluye_sin_revisar_y_desactualizadas()
    {
        Assert.Equal([Ecua, Lechon], VistaConciliacion.Aplicar(Todas, new CriteriosVista(Revision: CriteriosVista.RevisionPendientes)));
        Assert.Equal([Setel], VistaConciliacion.Aplicar(Todas, new CriteriosVista(Revision: CriteriosVista.RevisionRevisadas)));
    }

    [Fact]
    public void Ordena_por_texto_en_ambos_sentidos()
    {
        var asc = VistaConciliacion.Aplicar(Todas, new CriteriosVista(OrdenColumna: ColumnaConciliacion.Documento, OrdenAscendente: true));
        var desc = VistaConciliacion.Aplicar(Todas, new CriteriosVista(OrdenColumna: ColumnaConciliacion.Documento, OrdenAscendente: false));

        Assert.Equal(["001-053-000139279", "001-891-000000005", "002-001-000000010"], Documentos(asc));
        Assert.Equal(["002-001-000000010", "001-891-000000005", "001-053-000139279"], Documentos(desc));
    }

    [Fact]
    public void Ordena_por_total_numericamente_no_como_texto()
    {
        var r = VistaConciliacion.Aplicar(Todas, new CriteriosVista(OrdenColumna: ColumnaConciliacion.TotalSri, OrdenAscendente: true));

        Assert.Equal([Setel, Lechon, Ecua], r); // 35.45 < 62.08 < 974.32
    }

    [Fact]
    public void Las_filas_sin_uno_de_los_lados_van_primero_al_ordenar_ascendente_por_ese_total()
    {
        var soloSage = Fila(ClasificacionConciliacion.SoloEnSage, null, Sage("003-001-000000001", "PROV", 10m, postOrder: 9));

        var r = VistaConciliacion.Aplicar([Ecua, soloSage], new CriteriosVista(OrdenColumna: ColumnaConciliacion.TotalSri, OrdenAscendente: true));

        Assert.Equal([soloSage, Ecua], r);
    }

    [Fact]
    public void Solo_en_SRI_usa_la_serie_del_SRI_como_documento()
    {
        var soloSri = Fila(ClasificacionConciliacion.SoloEnSri, Sri(new string('4', 49), "005-005-000000077", "X", 5m), null);

        Assert.Equal("005-005-000000077", soloSri.Documento);
    }

    [Fact]
    public void Conciliado_sin_verificar_no_es_aceptable()
    {
        var coincide = Fila(ClasificacionConciliacion.CoincidePendienteDeVerificar, Sri(new string('5', 49), "1", "X", 1m), Sage("1", "X", 1m));

        Assert.False(coincide.Aceptable);
        Assert.True(Ecua.Aceptable);
    }
}
