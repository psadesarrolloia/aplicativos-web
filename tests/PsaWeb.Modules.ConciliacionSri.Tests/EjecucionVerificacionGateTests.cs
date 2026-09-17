using PsaWeb.Modules.ConciliacionSri;

namespace PsaWeb.Modules.ConciliacionSri.Tests;

public class EjecucionVerificacionGateTests
{
    private static ResumenVerificacionCorrida Resumen(int verificados = 0) => new(
        DateTimeOffset.Now, DateTimeOffset.Now,
        [new ResumenVerificacionEmpresa("1791111111001", "Empresa Demo", verificados, 0, 0, [])]);

    [Fact]
    public async Task Ejecuta_la_corrida_si_el_candado_esta_libre()
    {
        var gate = new EjecucionVerificacionGate();

        var resultado = await gate.EjecutarAsync(_ => Task.FromResult(Resumen(3)));

        Assert.True(resultado.Ejecuto);
        Assert.Equal(3, resultado.Resumen!.TotalVerificados);
        Assert.False(gate.EnCurso);
    }

    [Fact]
    public async Task Rechaza_una_segunda_corrida_mientras_la_primera_esta_en_curso()
    {
        var gate = new EjecucionVerificacionGate();
        var puedeContinuar = new TaskCompletionSource();
        var primeraEnCurso = new TaskCompletionSource();

        var primera = gate.EjecutarAsync(async _ =>
        {
            primeraEnCurso.SetResult();
            await puedeContinuar.Task;
            return Resumen();
        });

        await primeraEnCurso.Task;
        Assert.True(gate.EnCurso);

        var segunda = await gate.EjecutarAsync(_ => Task.FromResult(Resumen()));

        Assert.False(segunda.Ejecuto);
        Assert.Equal("Ya hay una verificación en curso.", segunda.Motivo);

        puedeContinuar.SetResult();
        var resultadoPrimera = await primera;
        Assert.True(resultadoPrimera.Ejecuto);
    }

    [Fact]
    public async Task Libera_el_candado_incluso_si_la_corrida_lanza_una_excepcion()
    {
        var gate = new EjecucionVerificacionGate();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.EjecutarAsync(new Func<CancellationToken, Task<ResumenVerificacionCorrida>>(
                _ => throw new InvalidOperationException("boom"))));

        Assert.False(gate.EnCurso);
        var siguiente = await gate.EjecutarAsync(_ => Task.FromResult(Resumen()));
        Assert.True(siguiente.Ejecuto);
    }
}
