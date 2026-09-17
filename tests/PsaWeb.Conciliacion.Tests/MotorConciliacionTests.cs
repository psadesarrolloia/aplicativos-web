using PsaWeb.Comprobantes.Compras;
using PsaWeb.Conciliacion.Data;

namespace PsaWeb.Conciliacion.Tests;

public class MotorConciliacionTests
{
    private const string Clave1 = "0109202601179111111100120010010000000011234567811";
    private const string Clave2 = "0209202601179111111100120010010000000021234567812";

    private static ComprobanteSriGuardado Sri(
        string clave, decimal subtotal = 100m, decimal iva = 12m, decimal total = 112m,
        DateOnly? fecha = null, string rucEmisor = "1791111111001") => new(
        Id: 1, ClaveAcceso: clave, RucEmisor: rucEmisor, RazonSocialEmisor: "PROVEEDOR DEMO S.A.",
        TipoComprobante: "Factura", SerieComprobante: "001-001-000000001",
        FechaAutorizacion: new DateTime(2026, 9, 1), FechaEmision: fecha ?? new DateOnly(2026, 9, 1),
        Subtotal: subtotal, Iva: iva, Total: total, NumeroDocumentoModificado: null,
        Estado: null, FechaVerificacionEstado: null);

    private static CompraSage Sage(
        string autorizacion, decimal subtotal = 100m, decimal iva = 12m, decimal total = 112m,
        DateOnly? fecha = null, string rucProveedor = "1791111111001") => new(
        PostOrder: 1, RucProveedor: rucProveedor, NombreProveedor: "PROVEEDOR DEMO S.A.",
        Referencia: "001-001-000000001", Fecha: fecha ?? new DateOnly(2026, 9, 1),
        Autorizacion: autorizacion, Subtotal: subtotal, Iva: iva, Total: total);

    [Fact]
    public void Match_exacto_queda_pendiente_de_verificar_estado()
    {
        var resultado = MotorConciliacion.Conciliar([Sri(Clave1)], [Sage(Clave1)]);

        var fila = Assert.Single(resultado);
        Assert.Equal(ClasificacionConciliacion.CoincidePendienteDeVerificar, fila.Clasificacion);
        Assert.Empty(fila.Diferencias);
    }

    [Fact]
    public void Comprobante_sin_compra_correspondiente_es_Solo_en_Sri()
    {
        var resultado = MotorConciliacion.Conciliar([Sri(Clave1)], []);

        var fila = Assert.Single(resultado);
        Assert.Equal(ClasificacionConciliacion.SoloEnSri, fila.Clasificacion);
        Assert.Null(fila.Sage);
    }

    [Fact]
    public void Compra_con_autorizacion_vacia_es_Solo_en_Sage()
    {
        var resultado = MotorConciliacion.Conciliar([], [Sage(autorizacion: "")]);

        var fila = Assert.Single(resultado);
        Assert.Equal(ClasificacionConciliacion.SoloEnSage, fila.Clasificacion);
        Assert.Null(fila.ClaveAcceso);
    }

    [Fact]
    public void Compra_con_autorizacion_tecleada_que_no_matchea_ninguna_es_Solo_en_Sage()
    {
        var resultado = MotorConciliacion.Conciliar([Sri(Clave1)], [Sage(Clave2)]);

        Assert.Equal(2, resultado.Count);
        Assert.Contains(resultado, f => f.Clasificacion == ClasificacionConciliacion.SoloEnSri);
        Assert.Contains(resultado, f => f.Clasificacion == ClasificacionConciliacion.SoloEnSage && f.ClaveAcceso == Clave2);
    }

    [Fact]
    public void Delta_de_un_centavo_queda_dentro_de_tolerancia()
    {
        var resultado = MotorConciliacion.Conciliar([Sri(Clave1, total: 112.00m)], [Sage(Clave1, total: 112.01m)]);

        var fila = Assert.Single(resultado);
        Assert.Equal(ClasificacionConciliacion.CoincidePendienteDeVerificar, fila.Clasificacion);
    }

    [Fact]
    public void Delta_de_un_dolar_marca_Valores_distintos_con_el_detalle()
    {
        var resultado = MotorConciliacion.Conciliar([Sri(Clave1, total: 112m)], [Sage(Clave1, total: 111m)]);

        var fila = Assert.Single(resultado);
        Assert.Equal(ClasificacionConciliacion.ValoresDistintos, fila.Clasificacion);
        Assert.Contains(fila.Diferencias, d => d.StartsWith("Total:"));
    }

    [Fact]
    public void Mismos_montos_pero_RUC_emisor_distinto_marca_Metadata_distinta()
    {
        var resultado = MotorConciliacion.Conciliar(
            [Sri(Clave1, rucEmisor: "1791111111001")], [Sage(Clave1, rucProveedor: "1799999999001")]);

        var fila = Assert.Single(resultado);
        Assert.Equal(ClasificacionConciliacion.MetadataDistinta, fila.Clasificacion);
        Assert.Contains(fila.Diferencias, d => d.StartsWith("RUC emisor:"));
    }

    [Fact]
    public void Mismos_montos_pero_fecha_distinta_marca_Metadata_distinta()
    {
        var resultado = MotorConciliacion.Conciliar(
            [Sri(Clave1, fecha: new DateOnly(2026, 9, 1))], [Sage(Clave1, fecha: new DateOnly(2026, 9, 2))]);

        var fila = Assert.Single(resultado);
        Assert.Equal(ClasificacionConciliacion.MetadataDistinta, fila.Clasificacion);
        Assert.Contains(fila.Diferencias, d => d.StartsWith("Fecha:"));
    }

    [Fact]
    public void Autorizacion_con_espacios_matchea_igual_tras_recortarla()
    {
        var resultado = MotorConciliacion.Conciliar([Sri(Clave1)], [Sage($"  {Clave1}  ")]);

        var fila = Assert.Single(resultado);
        Assert.Equal(ClasificacionConciliacion.CoincidePendienteDeVerificar, fila.Clasificacion);
    }

    [Fact]
    public void Valores_distintos_tiene_prioridad_sobre_metadata_distinta()
    {
        // Si además de la fecha difiere un monto, se reporta como ValoresDistintos
        // (más grave) y no se llega a evaluar la metadata.
        var resultado = MotorConciliacion.Conciliar(
            [Sri(Clave1, total: 112m, fecha: new DateOnly(2026, 9, 1))],
            [Sage(Clave1, total: 999m, fecha: new DateOnly(2026, 9, 2))]);

        var fila = Assert.Single(resultado);
        Assert.Equal(ClasificacionConciliacion.ValoresDistintos, fila.Clasificacion);
    }
}
