using PsaWeb.Comprobantes.Retenciones;
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Comprobantes.Tests;

public class ValidadorNumeroEstablecimientoTests
{
    private sealed class FakeEstab(EstablecimientoInfo? resultado) : IEstablecimientoLookup
    {
        public string? RucPedido;
        public string? CodigoPedido;
        public string? PuntoPedido;

        public Task<EstablecimientoInfo?> BuscarAsync(string ruc, string codigo, string puntoEmision, CancellationToken ct = default)
        {
            RucPedido = ruc;
            CodigoPedido = codigo;
            PuntoPedido = puntoEmision;
            return Task.FromResult(resultado);
        }
    }

    [Fact]
    public async Task Numero_17_valido_y_establecimiento_encontrado()
    {
        var estab = new FakeEstab(new EstablecimientoInfo(35, "001", "003", "Quito"));

        var v = await ValidadorNumeroEstablecimiento.CrearAsync("1791313747001", "001-003-000013538", estab);

        Assert.True(v.EsValido);
        Assert.Null(v.Error);
        Assert.Equal(35, v.EstablecimientoId);
        Assert.Equal("001", v.CodigoEstablecimiento);
        Assert.Equal("003", v.PuntoEmision);
        Assert.Equal("000013538", v.Secuencial);
        Assert.False(v.FueCorregido);

        Assert.Equal("1791313747001", estab.RucPedido);
        Assert.Equal("001", estab.CodigoPedido);
        Assert.Equal("003", estab.PuntoPedido);
    }

    [Fact]
    public async Task Numero_corto_pero_numerico_se_corrige()
    {
        var estab = new FakeEstab(new EstablecimientoInfo(1, "001", "003", "s/d"));

        var v = await ValidadorNumeroEstablecimiento.CrearAsync("1791313747001", "001-003-13538", estab);

        Assert.True(v.EsValido);
        Assert.True(v.FueCorregido);
        Assert.Equal("000013538", v.Secuencial);
    }

    [Fact]
    public async Task Formato_invalido_no_consulta_el_establecimiento()
    {
        var estab = new FakeEstab(null);

        var v = await ValidadorNumeroEstablecimiento.CrearAsync("1791313747001", "no-es-un-numero", estab);

        Assert.False(v.EsValido);
        Assert.Contains("Formato", v.Error);
        Assert.Null(estab.RucPedido); // ni se llamó al lookup
    }

    [Fact]
    public async Task Establecimiento_no_registrado_es_invalido()
    {
        var estab = new FakeEstab(null);

        var v = await ValidadorNumeroEstablecimiento.CrearAsync("1791313747001", "001-009-000000001", estab);

        Assert.False(v.EsValido);
        Assert.Contains("establecimiento registrado", v.Error);
        Assert.Equal("009", estab.PuntoPedido);
    }
}
