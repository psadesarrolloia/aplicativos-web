using System.Text.Json;
using PsaWeb.Datil;
using PsaWeb.Datil.Model;

namespace PsaWeb.Datil.Tests;

public class DatilSerializationTests
{
    private static Retencion Ejemplo() => new()
    {
        Secuencial = "123",
        FechaEmision = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.FromHours(-5)),
        Ambiente = 1,
        PeriodoFiscal = "08/2026",
        Emisor = new Emisor
        {
            Ruc = "1790352897001",
            RazonSocial = "EMPRESA S.A.",
            NombreComercial = "EMPRESA",
            Direccion = "Quito",
            ObligadoContabilidad = true,
            Establecimiento = new Establecimiento { Codigo = "001", PuntoEmision = "001", Direccion = "Quito" },
        },
        Sujeto = new Comprador
        {
            RazonSocial = "PROVEEDOR CIA LTDA",
            Identificacion = "1791709438001",
            TipoIdentificacion = "04",
            Email = "prov@ejemplo.com",
            Direccion = "Guayaquil",
        },
        Items =
        {
            new ItemRetencion
            {
                Codigo = "1", CodigoPorcentaje = "312", Porcentaje = 1.75,
                BaseImponible = 100, ValorRetenido = 1.75,
                FechaEmisionDocumentoSustento = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.FromHours(-5)),
                NumeroDocumentoSustento = "001-001-000000123", TipoDocumentoSustento = "01",
            },
        },
        InformacionAdicional = new Dictionary<string, string> { ["Observacion"] = "prueba" },
    };

    [Fact]
    public void Serializa_en_snake_case_sin_nulos()
    {
        var json = DatilJson.Serialize(Ejemplo());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("periodo_fiscal", out _));
        Assert.True(root.TryGetProperty("sujeto", out var sujeto));
        Assert.Equal("PROVEEDOR CIA LTDA", sujeto.GetProperty("razon_social").GetString());
        Assert.Equal("04", sujeto.GetProperty("tipo_identificacion").GetString());

        Assert.True(root.TryGetProperty("emisor", out var emisor));
        Assert.True(emisor.GetProperty("obligado_contabilidad").GetBoolean());
        Assert.Equal("001", emisor.GetProperty("establecimiento").GetProperty("punto_emision").GetString());

        var item = root.GetProperty("items")[0];
        Assert.Equal("312", item.GetProperty("codigo_porcentaje").GetString());
        Assert.Equal(1.75, item.GetProperty("valor_retenido").GetDouble());

        // clave_acceso es null => no debe aparecer
        Assert.False(root.TryGetProperty("clave_acceso", out _));

        // InformacionAdicional: la clave del diccionario va tal cual
        Assert.Equal("prueba", root.GetProperty("informacion_adicional").GetProperty("Observacion").GetString());
    }
}
