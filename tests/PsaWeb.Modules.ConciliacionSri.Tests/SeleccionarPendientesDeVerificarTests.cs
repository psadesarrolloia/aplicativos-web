using PsaWeb.Conciliacion;
using PsaWeb.Conciliacion.Data;

namespace PsaWeb.Modules.ConciliacionSri.Tests;

/// <summary>
/// La selección de candidatas a "Verificar pendientes" es lógica pura (sin
/// Sage/DB) — separada de <see cref="ProcesadorVerificacionEstado"/> justamente
/// para poder testearla así.
/// </summary>
public class SeleccionarPendientesDeVerificarTests
{
    private static readonly TimeSpan Umbral = TimeSpan.FromDays(7);
    private static readonly DateTime Ahora = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

    private static ComprobanteSriGuardado Sri(DateTime? fechaVerificacion = null, DateOnly? emision = null) => new(
        Id: 1, ClaveAcceso: new string('1', 49), RucEmisor: "1791111111001", RazonSocialEmisor: "PROVEEDOR DEMO S.A.",
        TipoComprobante: "Factura", SerieComprobante: "001-001-000000001",
        FechaAutorizacion: Ahora, FechaEmision: emision ?? DateOnly.FromDateTime(Ahora),
        Subtotal: 100, Iva: 12, Total: 112, NumeroDocumentoModificado: null,
        Estado: null, FechaVerificacionEstado: fechaVerificacion);

    private static FilaConciliacion Fila(ClasificacionConciliacion clasificacion, DateTime? fechaVerificacion = null) =>
        new(Sri(fechaVerificacion).ClaveAcceso, Sri(fechaVerificacion), null, clasificacion, []);

    [Theory]
    [InlineData(ClasificacionConciliacion.SoloEnSri)]
    [InlineData(ClasificacionConciliacion.CoincidePendienteDeVerificar)]
    [InlineData(ClasificacionConciliacion.ValoresDistintos)]
    [InlineData(ClasificacionConciliacion.MetadataDistinta)]
    public void Incluye_toda_fila_con_comprobante_del_SRI_sin_verificar(ClasificacionConciliacion clasificacion)
    {
        var resultado = ProcesadorVerificacionEstado.SeleccionarPendientesDeVerificar([Fila(clasificacion)], Umbral, Ahora);

        Assert.Single(resultado);
    }

    [Fact]
    public void Excluye_solo_en_Sage_porque_no_tiene_comprobante_del_SRI_que_consultar()
    {
        var soloSage = new FilaConciliacion(null, null, null, ClasificacionConciliacion.SoloEnSage, []);

        Assert.Empty(ProcesadorVerificacionEstado.SeleccionarPendientesDeVerificar([soloSage], Umbral, Ahora));
    }

    [Fact]
    public void Excluye_las_de_meses_que_el_WS_del_SRI_ya_no_responde()
    {
        // Hoy 17/09: el WS responde desde el 01/08. Una del 31/07 nunca se va a poder verificar.
        var vieja = new FilaConciliacion(null, Sri(emision: new DateOnly(2026, 7, 31)), null, ClasificacionConciliacion.CoincidePendienteDeVerificar, []);
        var enRango = new FilaConciliacion(null, Sri(emision: new DateOnly(2026, 8, 1)), null, ClasificacionConciliacion.CoincidePendienteDeVerificar, []);

        var resultado = ProcesadorVerificacionEstado.SeleccionarPendientesDeVerificar([vieja, enRango], Umbral, Ahora);

        Assert.Equal([enRango], resultado);
    }

    [Fact]
    public void Excluye_las_filas_sin_comprobante_del_SRI()
    {
        var soloSage = new FilaConciliacion(null, null, null, ClasificacionConciliacion.MetadataDistinta, []);

        Assert.Empty(ProcesadorVerificacionEstado.SeleccionarPendientesDeVerificar([soloSage], Umbral, Ahora));
    }

    [Fact]
    public void Excluye_las_verificadas_hace_menos_del_umbral()
    {
        var fila = Fila(ClasificacionConciliacion.ValoresDistintos, fechaVerificacion: Ahora.AddDays(-1));

        var resultado = ProcesadorVerificacionEstado.SeleccionarPendientesDeVerificar([fila], Umbral, Ahora);

        Assert.Empty(resultado);
    }

    [Fact]
    public void Reincluye_las_verificadas_hace_mas_del_umbral()
    {
        var fila = Fila(ClasificacionConciliacion.MetadataDistinta, fechaVerificacion: Ahora.AddDays(-8));

        var resultado = ProcesadorVerificacionEstado.SeleccionarPendientesDeVerificar([fila], Umbral, Ahora);

        Assert.Single(resultado);
    }
}
