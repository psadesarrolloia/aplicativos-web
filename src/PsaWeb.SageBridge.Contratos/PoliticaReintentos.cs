using System;

namespace PsaWeb.SageBridge.Contratos;

/// <summary>Cuántas veces y cada cuánto se reintenta un trabajo que falló por una causa pasajera.</summary>
public static class PoliticaReintentos
{
    /// <summary>Intentos totales (incluido el primero) antes de dejar el trabajo en Error.</summary>
    public const int MaximoIntentos = 5;

    /// <summary>Demora antes del intento siguiente: 2, 5, 10, 20 minutos.</summary>
    public static TimeSpan Demora(int intentosHechos)
    {
        switch (intentosHechos)
        {
            case <= 1: return TimeSpan.FromMinutes(2);
            case 2: return TimeSpan.FromMinutes(5);
            case 3: return TimeSpan.FromMinutes(10);
            default: return TimeSpan.FromMinutes(20);
        }
    }

    public static bool QuedanIntentos(int intentosHechos) => intentosHechos < MaximoIntentos;
}
