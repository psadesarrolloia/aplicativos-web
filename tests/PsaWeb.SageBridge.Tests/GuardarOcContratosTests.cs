using System.Reflection;
using System.Text.Json;
using PsaWeb.SageBridge.Contratos;

namespace PsaWeb.SageBridge.Tests;

public class NumeracionComprasTests
{
    [Fact]
    public void Siguiente_oc_usa_el_maximo_numerico_del_prefijo()
    {
        Assert.Equal("OC-8769", NumeracionCompras.SiguienteOc("OC", ["OC-8768", "OC-0999", "10-9999", "OC-ABC", " OC-8700 "]));
        Assert.Equal("OC-10000", NumeracionCompras.SiguienteOc("OC", ["OC-9999"])); // Corrección C4
        Assert.Equal("OC-10001", NumeracionCompras.SiguienteOc("OC", ["OC-9999", "OC-10000"]));
        Assert.Equal("OC-0001", NumeracionCompras.SiguienteOc("OC", []));
    }

    [Fact]
    public void Siguiente_retencion_por_serie_y_con_secuencial_inicial()
    {
        string[] existentes = ["001-001-000025829", "001-001-000025830", "002-001-000099999", "001-001-XX", ""];
        Assert.Equal("001-001-000025831", NumeracionCompras.SiguienteRetencion("001-001", existentes));
        Assert.Equal("002-001-000100000", NumeracionCompras.SiguienteRetencion("002-001", existentes));
        Assert.Equal("003-001-000000001", NumeracionCompras.SiguienteRetencion("003-001", existentes));
        Assert.Equal("003-001-000000500", NumeracionCompras.SiguienteRetencion("003-001", existentes, 500));
        Assert.Equal("001-001-000025831", NumeracionCompras.SiguienteRetencion("001-001", existentes, 500));
    }

    [Theory]
    [InlineData("001-001-000025829", true)]
    [InlineData("001-001-00002582", false)]
    [InlineData("001001-000025829", false)]
    public void Formato_de_retencion(string numero, bool valido) => Assert.Equal(valido, NumeracionCompras.EsNumeroRetencion(numero));

    [Theory]
    [InlineData("OC-8757", true)]
    [InlineData("OC-10000", true)]
    [InlineData("OC-875", false)]
    [InlineData("OC8757", false)]
    public void Formato_de_oc(string numero, bool valido) => Assert.Equal(valido, NumeracionCompras.EsNumeroOc(numero));
}

public class ContratosGuardarOcTests
{
    /// <summary>
    /// El Bridge lee el payload con DataContractJsonSerializer (orden alfabético de miembros); la web lo escribe con
    /// System.Text.Json (orden de declaración). Se exige que coincidan.
    /// </summary>
    [Theory]
    [InlineData(typeof(PayloadGuardarOc))]
    [InlineData(typeof(LineaOcContrato))]
    [InlineData(typeof(ProveedorContrato))]
    [InlineData(typeof(ResultadoGuardarOc))]
    public void Las_propiedades_estan_en_orden_alfabetico(Type tipo)
    {
        var nombres = tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.MetadataToken).Select(p => p.Name).ToList();
        Assert.Equal(nombres.OrderBy(n => n, StringComparer.Ordinal), nombres);
    }

    [Fact]
    public void Payload_ida_y_vuelta_con_system_text_json()
    {
        var p = new PayloadGuardarOc
        {
            NumeroFactura = "001-001-000000001",
            Lineas = [new LineaOcContrato { Tipo = "Detalle", Item = "C1", Cantidad = 4.627m, Monto = 13.04m, PrecioUnitario = 13.04m / 4.627m }],
            Proveedor = new ProveedorContrato { Id = "PROV", EsNuevo = true },
        };
        var vuelta = JsonSerializer.Deserialize<PayloadGuardarOc>(JsonSerializer.Serialize(p))!;
        Assert.Equal(p.Lineas[0].PrecioUnitario, vuelta.Lineas[0].PrecioUnitario);
        Assert.True(vuelta.Proveedor.EsNuevo);
    }

    [Fact]
    public void Errores_odbc_se_reintentan_sin_reciclar()
    {
        Assert.Equal(AccionAnteError.Reintentar,
            ClasificadorErroresSage.Clasificar("OdbcException", "ERROR [HY000] [Zen][ODBC Client Interface] Btrieve error 3006"));
    }
}
