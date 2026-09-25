using PsaWeb.SageBridge.Contratos;

namespace PsaWeb.SageBridge.Tests;

public class VentanaMantenimientoTests
{
    [Theory]
    [InlineData("22:00-06:00", "2026-09-25 23:30", true)]
    [InlineData("22:00-06:00", "2026-09-25 02:00", true)]
    [InlineData("22:00-06:00", "2026-09-25 06:00", false)] // el fin es exclusivo
    [InlineData("22:00-06:00", "2026-09-25 21:59", false)]
    [InlineData("22:00-06:00", "2026-09-25 12:00", false)]
    [InlineData("13:00-14:00", "2026-09-25 13:30", true)]
    [InlineData("13:00-14:00", "2026-09-25 14:00", false)]
    [InlineData("", "2026-09-25 03:00", false)] // sin ventana
    public void Contiene_respeta_el_cruce_de_medianoche(string texto, string ahora, bool esperado)
    {
        var ventana = VentanaMantenimiento.Parsear(texto);
        Assert.Equal(esperado, ventana.Contiene(DateTime.Parse(ahora)));
    }

    [Fact]
    public void FinDesde_devuelve_el_fin_de_la_ventana_en_curso()
    {
        var ventana = VentanaMantenimiento.Parsear("22:00-06:00");
        Assert.Equal(new DateTime(2026, 9, 26, 6, 0, 0), ventana.FinDesde(new DateTime(2026, 9, 25, 23, 0, 0)));
        Assert.Equal(new DateTime(2026, 9, 26, 6, 0, 0), ventana.FinDesde(new DateTime(2026, 9, 26, 1, 0, 0)));
        Assert.Null(ventana.FinDesde(new DateTime(2026, 9, 25, 12, 0, 0)));
    }

    [Theory]
    [InlineData("22-06")]
    [InlineData("25:00-06:00")]
    [InlineData("22:00")]
    [InlineData("aa:bb-cc:dd")]
    public void Rechaza_formatos_invalidos(string texto)
    {
        Assert.False(VentanaMantenimiento.TryParsear(texto, out _));
        Assert.Throws<FormatException>(() => VentanaMantenimiento.Parsear(texto));
    }

    [Fact]
    public void ToString_vuelve_al_formato_de_entrada()
    {
        Assert.Equal("22:00-06:00", VentanaMantenimiento.Parsear(" 22:00 - 06:00 ").ToString());
        Assert.Equal(string.Empty, VentanaMantenimiento.Ninguna.ToString());
    }
}

public class ClasificadorErroresSageTests
{
    [Theory]
    // Textos reales vistos en la F0 (§14 del plan).
    [InlineData("PeachtreeException", "Pervasive.Data.SqlClient.Lna.v: [LNA][Zen][SQL Engine][Data Record Manager]The MicroKernel cannot find the specified file(Btrieve Error 12)", AccionAnteError.ReciclarProceso)]
    [InlineData("InvalidOperationException", "You must call SetDefaultDatabase before trying to use the default connection.", AccionAnteError.ReciclarProceso)]
    [InlineData("AuthorizationException", "Authentication failed.", AccionAnteError.SinAutorizacion)]
    [InlineData("CompanySharedAccessException", "Company can not be opened for shared access.", AccionAnteError.Reintentar)]
    [InlineData("PeachtreeException", "Another user or application is processing data in Sage 50.", AccionAnteError.Reintentar)]
    [InlineData("LicenseNotAvailableException", "No license.", AccionAnteError.Reintentar)]
    [InlineData("ValidationException", "Vendor ID is required.", AccionAnteError.Fallar)]
    [InlineData(null, null, AccionAnteError.Fallar)]
    public void Clasifica_por_tipo_y_mensaje(string? tipo, string? mensaje, AccionAnteError esperado)
    {
        Assert.Equal(esperado, ClasificadorErroresSage.Clasificar(tipo, mensaje));
    }
}

public class PoliticaReintentosTests
{
    [Fact]
    public void Demoras_crecientes_y_tope_de_intentos()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), PoliticaReintentos.Demora(1));
        Assert.Equal(TimeSpan.FromMinutes(5), PoliticaReintentos.Demora(2));
        Assert.Equal(TimeSpan.FromMinutes(10), PoliticaReintentos.Demora(3));
        Assert.Equal(TimeSpan.FromMinutes(20), PoliticaReintentos.Demora(9));
        Assert.True(PoliticaReintentos.QuedanIntentos(PoliticaReintentos.MaximoIntentos - 1));
        Assert.False(PoliticaReintentos.QuedanIntentos(PoliticaReintentos.MaximoIntentos));
    }

    [Fact]
    public void Solo_ProbarEmpresa_se_procesa_sin_habilitar()
    {
        Assert.True(TiposTrabajo.PermitidoSinHabilitar(TiposTrabajo.ProbarEmpresa));
        Assert.False(TiposTrabajo.PermitidoSinHabilitar("CrearOc"));
        Assert.True(EstadosTrabajo.EsFinal(EstadosTrabajo.Hecho));
        Assert.False(EstadosTrabajo.EsFinal(EstadosTrabajo.EnProceso));
    }
}
