using PsaWeb.SageBridge.Cola.Data;

namespace PsaWeb.SageBridge.Cola;

/// <summary>Lectura del latido del Bridge para mostrar en la web.</summary>
public static class EstadoBridge
{
    /// <summary>
    /// El trabajador late en cada vuelta de su ciclo (≤ 15 s) salvo mientras procesa un lote; abrir una
    /// compañía cuesta ~10 s y un lote puede tardar algo más. Sin latido en 3 minutos se lo da por caído.
    /// </summary>
    public static readonly TimeSpan UmbralSinLatido = TimeSpan.FromMinutes(3);

    public static bool EstaVivo(LatidoBridge latido, DateTime ahoraUtc) =>
        ahoraUtc - latido.UltimoLatidoUtc <= UmbralSinLatido;
}
