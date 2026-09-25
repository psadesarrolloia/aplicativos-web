using System;

namespace PsaWeb.SageBridge.Contratos;

/// <summary>Qué hacer con un trabajo cuando el SDK de Sage lanza una excepción.</summary>
public enum AccionAnteError
{
    /// <summary>Error propio del trabajo (datos, validación de Sage): queda en Error, no se reintenta solo.</summary>
    Fallar,

    /// <summary>La compañía está ocupada (backup, modo exclusivo, otro proceso): se reintenta más tarde.</summary>
    Reintentar,

    /// <summary>Sage no autorizó al Bridge en esa compañía: queda en Error con instrucciones para aprobarlo en Sage.</summary>
    SinAutorizacion,

    /// <summary>
    /// Error de base de datos del motor (Btrieve/Zen). En la F0 se vio que deja inservible TODO el proceso
    /// (las llamadas siguientes fallan con «You must call SetDefaultDatabase…» aun con sesiones nuevas):
    /// el trabajo se reintenta y el proceso trabajador se recicla.
    /// </summary>
    ReciclarProceso,
}

/// <summary>
/// Clasifica una excepción del SDK por su tipo y su mensaje (texto, para no depender del SDK en este ensamblado).
/// Los textos salen de lo observado en la F0 (§14 del plan) y de la documentación del SDK 2023.
/// </summary>
public static class ClasificadorErroresSage
{
    public static AccionAnteError Clasificar(string? tipoExcepcion, string? mensaje)
    {
        var tipo = tipoExcepcion ?? string.Empty;
        var texto = mensaje ?? string.Empty;

        if (Contiene(texto, "Btrieve") || Contiene(texto, "MicroKernel") || Contiene(texto, "SetDefaultDatabase")
            || Contiene(texto, "[Zen]") || Contiene(texto, "Pervasive"))
        {
            return AccionAnteError.ReciclarProceso;
        }

        if (Contiene(tipo, "Authorization") || Contiene(texto, "Authentication failed") || Contiene(texto, "NoCredentials"))
        {
            return AccionAnteError.SinAutorizacion;
        }

        if (Contiene(tipo, "CompanySharedAccess") || Contiene(tipo, "LicenseNotAvailable") || Contiene(tipo, "CompanyLocked")
            || Contiene(texto, "shared access") || Contiene(texto, "Another user or application")
            || Contiene(texto, "exclusive") || Contiene(texto, "locked"))
        {
            return AccionAnteError.Reintentar;
        }

        return AccionAnteError.Fallar;
    }

    private static bool Contiene(string texto, string buscado) =>
        texto.IndexOf(buscado, StringComparison.OrdinalIgnoreCase) >= 0;
}
