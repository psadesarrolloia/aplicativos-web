using PsaWeb.Modules.Retenciones;

namespace PsaWeb.Modules.Retenciones.Tests;

public class EjecucionRetencionesGateTests
{
    private static ResumenCorrida ResumenVacio() =>
        new(DateTimeOffset.Now, DateTimeOffset.Now, DryRun: true, Array.Empty<ResumenEmpresa>());

    [Fact]
    public async Task Segunda_corrida_concurrente_se_rechaza_sin_esperar()
    {
        var gate = new EjecucionRetencionesGate();
        var arranco = new TaskCompletionSource();
        var libera = new TaskCompletionSource();

        var primera = gate.EjecutarAsync("a", async _ =>
        {
            arranco.SetResult();
            await libera.Task;
            return ResumenVacio();
        });

        await arranco.Task; // la primera ya tiene el candado

        var segunda = await gate.EjecutarAsync("b", _ => Task.FromResult(ResumenVacio()));
        Assert.False(segunda.Ejecuto);
        Assert.NotNull(segunda.Motivo);
        Assert.True(gate.Estado.EnCurso);
        Assert.Equal("a", gate.Estado.OrigenActual);

        libera.SetResult();
        var resultadoPrimera = await primera;
        Assert.True(resultadoPrimera.Ejecuto);
        Assert.False(gate.Estado.EnCurso);
    }

    [Fact]
    public async Task Tras_terminar_el_candado_queda_libre_para_la_siguiente()
    {
        var gate = new EjecucionRetencionesGate();

        var uno = await gate.EjecutarAsync("a", _ => Task.FromResult(ResumenVacio()));
        var dos = await gate.EjecutarAsync("b", _ => Task.FromResult(ResumenVacio()));

        Assert.True(uno.Ejecuto);
        Assert.True(dos.Ejecuto);
        Assert.Equal("b", gate.Estado.OrigenUltima);
    }

    [Fact]
    public async Task Si_la_corrida_lanza_libera_el_candado_y_guarda_el_error()
    {
        var gate = new EjecucionRetencionesGate();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.EjecutarAsync("a", _ => throw new InvalidOperationException("boom")));

        Assert.False(gate.Estado.EnCurso);
        Assert.Equal("boom", gate.Estado.ErrorUltima);

        var siguiente = await gate.EjecutarAsync("b", _ => Task.FromResult(ResumenVacio()));
        Assert.True(siguiente.Ejecuto);
    }
}
