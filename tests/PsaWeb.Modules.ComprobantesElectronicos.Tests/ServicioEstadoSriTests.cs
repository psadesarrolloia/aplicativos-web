using PsaWeb.Conciliacion;
using PsaWeb.Modules.ComprobantesElectronicos.Estado;

namespace PsaWeb.Modules.ComprobantesElectronicos.Tests;

/// <summary>
/// Reglas puras del estado en el SRI, medidas el 2026-09-19 contra el WS real:
/// el WS solo responde por el mes en curso + el anterior; el alcance es de 2 años.
/// </summary>
public class ServicioEstadoSriTests
{
    private static readonly DateTime Hoy = new(2026, 9, 19);
    private const string Clave = "0109202601179131374700120010030000134872794314815"; // 49 dígitos

    [Theory]
    [InlineData("2026-09-19", "2026-08-01")]
    [InlineData("2026-10-01", "2026-09-01")] // al cambiar de mes el rango se corre
    [InlineData("2026-01-15", "2025-12-01")]
    public void El_rango_del_SRI_empieza_el_primer_dia_del_mes_anterior(string hoy, string esperado)
        => Assert.Equal(DateTime.Parse(esperado), ServicioEstadoSri.InicioRangoSri(DateTime.Parse(hoy)));

    [Theory]
    [InlineData("2026-07-31", false)] // medido: rechazado
    [InlineData("2026-08-01", true)]  // medido: aceptado
    [InlineData("2026-09-19", true)]
    public void Frontera_del_rango_del_SRI_medida_en_produccion(string emision, bool enRango)
        => Assert.Equal(enRango, ServicioEstadoSri.EnRangoSri(DateTime.Parse(emision), Hoy));

    [Theory]
    [InlineData("2024-09-19", true)]
    [InlineData("2024-09-18", false)]
    [InlineData("2019-04-01", false)]
    public void Alcance_de_verificacion_es_de_dos_anios(string emision, bool enAlcance)
        => Assert.Equal(enAlcance, ServicioEstadoSri.EnAlcance(DateTime.Parse(emision), Hoy));

    [Fact]
    public void ClaveDesdeJson_extrae_la_clave_de_49_digitos()
        => Assert.Equal(Clave, ServicioEstadoSri.ClaveDesdeJson($$"""{"pagos":[],"clave_acceso":"{{Clave}}","id":"x"}"""));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("[1,2]")]
    [InlineData("""{"id":"x"}""")]
    [InlineData("""{"clave_acceso":"123"}""")]
    [InlineData("""{"clave_acceso":"01092026011791313747001200100300001348727943148AB"}""")]
    public void ClaveDesdeJson_devuelve_null_si_no_hay_una_clave_valida(string? json)
        => Assert.Null(ServicioEstadoSri.ClaveDesdeJson(json));

    [Theory]
    [InlineData("AUTORIZADO", EstadoComprobanteSri.Autorizado)]
    [InlineData("autorizado", EstadoComprobanteSri.Autorizado)]
    [InlineData("ANULADO", EstadoComprobanteSri.Anulado)]
    [InlineData("NO AUTORIZADO", EstadoComprobanteSri.NoAutorizado)]
    [InlineData("EN PROCESO", EstadoComprobanteSri.Otro)]   // valor desconocido: no se asume
    [InlineData(null, EstadoComprobanteSri.ErrorServicio)]
    public void EstadoDesdeDatil_usa_el_mismo_vocabulario_del_SRI(string? datil, EstadoComprobanteSri esperado)
        => Assert.Equal(esperado, ServicioEstadoSri.EstadoDesdeDatil(datil));

    private static ComprobanteAVerificar C(int id, short ambiente = 2, string emision = "2026-09-01") =>
        new(TipoComprobante.Factura, "1791313747001", id, DateTime.Parse(emision), "datil", ambiente);

    [Fact]
    public void Candidatos_son_los_de_produccion_dentro_del_alcance_sin_verificar_o_vencidos()
    {
        var ahora = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
        var guardados = new Dictionary<(string Ruc, int RefId), EstadoSri>
        {
            [("1791313747001", 3)] = new("Autorizado", "SRI", null, ahora.AddDays(-1), Clave),   // reciente → no
            [("1791313747001", 4)] = new("Autorizado", "SRI", null, ahora.AddDays(-10), Clave),  // vencido → sí
        };

        var candidatos = ServicioEstadoSri.SeleccionarCandidatos(
            new[]
            {
                C(1),                          // sin verificar → sí
                C(2, ambiente: 1),             // pruebas → no
                C(3),
                C(4),
                C(5, emision: "2024-01-01"),   // fuera de los 2 años → no
            },
            guardados, TimeSpan.FromDays(7), Hoy, ahora);

        Assert.Equal(new[] { 1, 4 }, candidatos.Select(c => c.RefId).OrderBy(x => x));
    }
}
