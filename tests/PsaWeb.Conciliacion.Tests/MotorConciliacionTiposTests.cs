using PsaWeb.Comprobantes.Compras;
using PsaWeb.Conciliacion.Data;

namespace PsaWeb.Conciliacion.Tests;

/// <summary>Notas de crédito y retenciones recibidas (además de las facturas de compra) en el motor.</summary>
public class MotorConciliacionTiposTests
{
    private const string ClaveNc = "1808202604179188956800120011000000002734525623419";
    private const string ClaveRet = "0608202607179201072100120010050000359306000125718";
    private const string ClaveFactura = "0109202601179111111100120010010000000011234567811";

    private static ComprobanteSriGuardado SriDe(
        string clave, string tipoTexto, string serie, string rucEmisor, decimal subtotal, decimal iva, decimal total,
        string? docModificado = null, DateOnly? fecha = null) => new(
        Id: 1, ClaveAcceso: clave, RucEmisor: rucEmisor, RazonSocialEmisor: "EMISOR",
        TipoComprobante: tipoTexto, SerieComprobante: serie,
        FechaAutorizacion: new DateTime(2026, 8, 18), FechaEmision: fecha ?? new DateOnly(2026, 8, 18),
        Subtotal: subtotal, Iva: iva, Total: total, NumeroDocumentoModificado: docModificado,
        Estado: null, FechaVerificacionEstado: null);

    private static ComprobanteSriGuardado NcSri(string? docModificado = "001-100-000006645") =>
        SriDe(ClaveNc, "Nota de Crédito", "001-100-000000273", "1791889568001", 5336.68m, 0m, 5336.68m, docModificado);

    private static CompraSage NcSage(string autorizacion = ClaveNc, string? docModificado = "001-100-000006645",
        decimal total = 5336.68m) => new(
        PostOrder: 10, RucProveedor: "1791889568001", NombreProveedor: "EXPRESSCHASQUIS SA", Referencia: "001-100-000000273",
        Fecha: new DateOnly(2026, 8, 18), Autorizacion: autorizacion, Subtotal: total, Iva: 0m, Total: total,
        Tipo: TipoDocumentoRecibido.NotaCredito, DocumentoModificado: docModificado);

    private static ComprobanteSriGuardado RetSri(string serie = "001-005-000035930", string ruc = "1792010721001", string clave = ClaveRet) =>
        SriDe(clave, "Comprobante de Retención", serie, ruc, 0m, 0m, 0m, docModificado: "28450870306", fecha: new DateOnly(2026, 8, 6));

    private static CompraSage RetSage(string autorizacion = "", string referencia = "001-005-000035930",
        string ruc = "1792010721001", decimal total = 8250.55m, long postOrder = 20) => new(
        PostOrder: postOrder, RucProveedor: ruc, NombreProveedor: "CONSORCIO PETROLERO BLOQUE 17", Referencia: referencia,
        Fecha: new DateOnly(2026, 8, 6), Autorizacion: autorizacion, Subtotal: 0m, Iva: 0m, Total: total,
        Tipo: TipoDocumentoRecibido.Retencion);

    // ---------------------------------------------------------------- tipo de documento

    [Theory]
    [InlineData("Factura", TipoDocumentoRecibido.Factura)]
    [InlineData("Nota de Crédito", TipoDocumentoRecibido.NotaCredito)]
    [InlineData("Nota de Cr�dito", TipoDocumentoRecibido.NotaCredito)] // tilde mal decodificada
    [InlineData("Comprobante de Retención", TipoDocumentoRecibido.Retencion)]
    [InlineData("Comprobante de Retenci�n", TipoDocumentoRecibido.Retencion)]
    [InlineData("Liquidación de Compra", TipoDocumentoRecibido.Otro)]
    [InlineData("Nota de Débito", TipoDocumentoRecibido.Otro)]
    [InlineData("", TipoDocumentoRecibido.Otro)]
    public void El_tipo_se_deduce_del_texto_del_reporte(string texto, TipoDocumentoRecibido esperado)
    {
        Assert.Equal(esperado, TiposDocumentoRecibido.De(texto));
    }

    // ---------------------------------------------------------------- notas de crédito

    [Fact]
    public void Nota_de_credito_que_coincide_en_todo_queda_pendiente_de_verificar()
    {
        var r = MotorConciliacion.Conciliar([NcSri()], [NcSage()]);

        var fila = Assert.Single(r);
        Assert.Equal(ClasificacionConciliacion.CoincidePendienteDeVerificar, fila.Clasificacion);
        Assert.Equal(TipoDocumentoRecibido.NotaCredito, fila.Tipo);
    }

    [Fact]
    public void Nota_de_credito_con_otro_documento_modificado_es_metadata_distinta()
    {
        var r = MotorConciliacion.Conciliar([NcSri("001-100-000006645")], [NcSage(docModificado: "001-100-000009999")]);

        var fila = Assert.Single(r);
        Assert.Equal(ClasificacionConciliacion.MetadataDistinta, fila.Clasificacion);
        Assert.Contains(fila.Diferencias, d => d.StartsWith("Documento modificado:") && d.Contains("001-100-000009999"));
    }

    [Fact]
    public void Si_Sage_no_trae_documento_modificado_no_se_marca_diferencia()
    {
        var r = MotorConciliacion.Conciliar([NcSri()], [NcSage(docModificado: null)]);

        Assert.Equal(ClasificacionConciliacion.CoincidePendienteDeVerificar, Assert.Single(r).Clasificacion);
    }

    [Fact]
    public void Nota_de_credito_con_otro_monto_es_valores_distintos()
    {
        var r = MotorConciliacion.Conciliar([NcSri()], [NcSage(total: 5000m)]);

        Assert.Equal(ClasificacionConciliacion.ValoresDistintos, Assert.Single(r).Clasificacion);
    }

    [Fact]
    public void Si_el_SRI_dice_factura_y_Sage_tiene_nota_de_credito_se_marca_el_tipo()
    {
        var factura = SriDe(ClaveNc, "Factura", "001-100-000000273", "1791889568001", 5336.68m, 0m, 5336.68m);

        var r = MotorConciliacion.Conciliar([factura], [NcSage()]);

        var fila = Assert.Single(r);
        Assert.Equal(ClasificacionConciliacion.MetadataDistinta, fila.Clasificacion);
        Assert.Contains(fila.Diferencias, d => d.StartsWith("Tipo:"));
    }

    // ---------------------------------------------------------------- retenciones

    [Fact]
    public void Retencion_sin_clave_en_Sage_cruza_por_ruc_y_serie_y_no_compara_montos()
    {
        // El SRI no trae montos (0) y Sage tiene 8250.55: no es una diferencia de valores.
        var r = MotorConciliacion.Conciliar([RetSri()], [RetSage(autorizacion: "")]);

        var fila = Assert.Single(r); // la retención de Sage NO reaparece como "Solo en Sage"
        Assert.Equal(ClasificacionConciliacion.CoincidePendienteDeVerificar, fila.Clasificacion);
        Assert.Equal(TipoDocumentoRecibido.Retencion, fila.Tipo);
    }

    [Fact]
    public void Retencion_con_la_clave_correcta_en_Sage_cruza_por_clave()
    {
        var r = MotorConciliacion.Conciliar([RetSri()], [RetSage(autorizacion: ClaveRet)]);

        Assert.Equal(ClasificacionConciliacion.CoincidePendienteDeVerificar, Assert.Single(r).Clasificacion);
    }

    [Fact]
    public void Retencion_cruzada_por_serie_con_otra_clave_tecleada_se_marca()
    {
        var r = MotorConciliacion.Conciliar([RetSri()], [RetSage(autorizacion: new string('9', 49))]);

        var fila = Assert.Single(r);
        Assert.Equal(ClasificacionConciliacion.MetadataDistinta, fila.Clasificacion);
        Assert.Contains(fila.Diferencias, d => d.StartsWith("Clave de acceso:"));
    }

    [Fact]
    public void Retencion_con_una_fecha_en_Sage_fuera_del_plazo_es_metadata_distinta()
    {
        var sage = RetSage() with { Fecha = new DateOnly(2026, 8, 14) }; // 8 días después: más que el plazo

        var r = MotorConciliacion.Conciliar([RetSri()], [sage]);

        var fila = Assert.Single(r);
        Assert.Equal(ClasificacionConciliacion.MetadataDistinta, fila.Clasificacion);
        Assert.Contains(fila.Diferencias, d => d.StartsWith("Fecha:"));
    }

    [Fact]
    public void Retencion_del_SRI_sin_par_en_Sage_es_Solo_en_SRI()
    {
        var r = MotorConciliacion.Conciliar([RetSri(serie: "001-005-000099999", clave: new string('7', 49))], [RetSage()]);

        Assert.Equal(2, r.Count);
        Assert.Contains(r, f => f.Clasificacion == ClasificacionConciliacion.SoloEnSri && f.Tipo == TipoDocumentoRecibido.Retencion);
        Assert.Contains(r, f => f.Clasificacion == ClasificacionConciliacion.SoloEnSage && f.Tipo == TipoDocumentoRecibido.Retencion);
    }

    [Fact]
    public void El_cruce_por_serie_no_aplica_a_las_facturas()
    {
        // Una factura cuya clave está mal tecleada en Sage NO debe taparse cruzando por serie.
        var sri = SriDe(ClaveFactura, "Factura", "001-001-000000001", "1791111111001", 100m, 12m, 112m);
        var sage = new CompraSage(1, "1791111111001", "PROV", "001-001-000000001", new DateOnly(2026, 8, 18),
            new string('5', 49), 100m, 12m, 112m);

        var r = MotorConciliacion.Conciliar([sri], [sage]);

        Assert.Equal(2, r.Count);
        Assert.Contains(r, f => f.Clasificacion == ClasificacionConciliacion.SoloEnSri);
        Assert.Contains(r, f => f.Clasificacion == ClasificacionConciliacion.SoloEnSage);
    }

    // ---------------------------------------------------------------- plazo de 5 días de las retenciones

    [Theory]
    [InlineData(0, ClasificacionConciliacion.CoincidePendienteDeVerificar)]
    [InlineData(5, ClasificacionConciliacion.CoincidePendienteDeVerificar)]  // el plazo del SRI: normal
    [InlineData(-5, ClasificacionConciliacion.CoincidePendienteDeVerificar)] // Sage puede ir antes o después
    [InlineData(6, ClasificacionConciliacion.MetadataDistinta)]
    [InlineData(-6, ClasificacionConciliacion.MetadataDistinta)]
    public void La_fecha_de_una_retencion_puede_diferir_hasta_el_plazo_entre_SRI_y_Sage(int dias, ClasificacionConciliacion esperada)
    {
        var sage = RetSage() with { Fecha = new DateOnly(2026, 8, 6).AddDays(dias) };

        var r = MotorConciliacion.Conciliar([RetSri()], [sage]);

        Assert.Equal(esperada, Assert.Single(r).Clasificacion);
    }

    [Fact]
    public void En_una_factura_cualquier_diferencia_de_fecha_sigue_marcandose()
    {
        var sri = SriDe(ClaveFactura, "Factura", "001-001-000000001", "1791111111001", 100m, 12m, 112m, fecha: new DateOnly(2026, 8, 6));
        var sage = new CompraSage(1, "1791111111001", "PROV", "001-001-000000001", new DateOnly(2026, 8, 7),
            ClaveFactura, 100m, 12m, 112m);

        var r = MotorConciliacion.Conciliar([sri], [sage]);

        Assert.Equal(ClasificacionConciliacion.MetadataDistinta, Assert.Single(r).Clasificacion);
    }

    [Fact]
    public void Retencion_solo_en_SRI_dentro_del_plazo_queda_en_plazo()
    {
        var hoy = new DateOnly(2026, 8, 9); // la retención es del 06/08: 3 días

        var r = MotorConciliacion.Conciliar([RetSri()], [], hoy: hoy, plazoRetencionDias: 5);

        var fila = Assert.Single(r);
        Assert.Equal(ClasificacionConciliacion.SoloEnSri, fila.Clasificacion);
        Assert.True(fila.EnPlazo);
    }

    [Fact]
    public void Retencion_solo_en_SRI_pasado_el_plazo_ya_no_esta_en_plazo()
    {
        var r = MotorConciliacion.Conciliar([RetSri()], [], hoy: new DateOnly(2026, 8, 12), plazoRetencionDias: 5); // 6 días

        Assert.False(Assert.Single(r).EnPlazo);
    }

    [Fact]
    public void Retencion_solo_en_Sage_dentro_del_plazo_queda_en_plazo_tenga_o_no_clave()
    {
        var hoy = new DateOnly(2026, 8, 8);

        var sinClave = MotorConciliacion.Conciliar([], [RetSage(autorizacion: "")], hoy: hoy, plazoRetencionDias: 5);
        var conClave = MotorConciliacion.Conciliar([], [RetSage(autorizacion: ClaveRet)], hoy: hoy, plazoRetencionDias: 5);

        Assert.True(Assert.Single(sinClave).EnPlazo);
        Assert.True(Assert.Single(conClave).EnPlazo);
    }

    [Fact]
    public void Facturas_y_notas_de_credito_nunca_quedan_en_plazo()
    {
        var factura = SriDe(ClaveFactura, "Factura", "001-001-000000001", "1791111111001", 100m, 12m, 112m);

        var r = MotorConciliacion.Conciliar([factura, NcSri()], [], hoy: new DateOnly(2026, 8, 18), plazoRetencionDias: 5);

        Assert.All(r, f => Assert.False(f.EnPlazo));
    }

    [Fact]
    public void Sin_fecha_de_hoy_no_se_calcula_el_plazo()
    {
        Assert.False(Assert.Single(MotorConciliacion.Conciliar([RetSri()], [])).EnPlazo);
    }

    [Fact]
    public void Recortar_al_periodo_deja_las_retenciones_cuya_fecha_del_SRI_o_de_Sage_esta_dentro()
    {
        var desde = new DateOnly(2026, 9, 1);
        var hasta = new DateOnly(2026, 9, 30);
        // Pareja: SRI 30/08 (fuera) con Sage 02/09 (dentro) -> se queda.
        var pareja = new FilaConciliacion(ClaveRet, RetSri() with { FechaEmision = new DateOnly(2026, 8, 30) },
            RetSage() with { Fecha = new DateOnly(2026, 9, 2) }, ClasificacionConciliacion.CoincidePendienteDeVerificar, []);
        // Solo en Sage del 28/08 (solo entró por la ventana ampliada) -> se va.
        var fuera = new FilaConciliacion(null, null, RetSage() with { Fecha = new DateOnly(2026, 8, 28), PostOrder = 99 },
            ClasificacionConciliacion.SoloEnSage, []);
        // Una factura no se recorta acá (ya venía acotada).
        var factura = new FilaConciliacion(ClaveNc, NcSri() with { FechaEmision = new DateOnly(2026, 8, 18) }, null,
            ClasificacionConciliacion.SoloEnSri, []);

        var r = MotorConciliacion.RecortarAlPeriodo([pareja, fuera, factura], desde, hasta);

        Assert.Equal([pareja, factura], r);
    }

    [Fact]
    public void Dos_retenciones_del_mismo_emisor_con_series_distintas_no_se_confunden()
    {
        var r = MotorConciliacion.Conciliar(
            [RetSri(serie: "001-005-000000001", clave: new string('1', 49)), RetSri(serie: "001-005-000000002", clave: new string('2', 49))],
            [RetSage(referencia: "001-005-000000002", postOrder: 31), RetSage(referencia: "001-005-000000001", postOrder: 30)]);

        Assert.Equal(2, r.Count);
        Assert.All(r, f => Assert.Equal(ClasificacionConciliacion.CoincidePendienteDeVerificar, f.Clasificacion));
    }
}
