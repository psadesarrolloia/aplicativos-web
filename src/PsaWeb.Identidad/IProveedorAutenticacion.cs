namespace PsaWeb.Identidad;

/// <summary>Resultado de un intento de inicio de sesión.</summary>
public sealed record ResultadoLogin(
    bool Exito,
    bool RequiereSegundoFactor,
    bool Bloqueado,
    bool NoPermitido,
    TimeSpan? BloqueoRestante = null,
    string? Motivo = null)
{
    public static readonly ResultadoLogin Correcto = new(true, false, false, false);
    public static readonly ResultadoLogin PideSegundoFactor = new(false, true, false, false);

    public static ResultadoLogin Invalido(string motivo = "Usuario o clave inválidos.")
        => new(false, false, false, false, Motivo: motivo);

    public static ResultadoLogin Deshabilitado()
        => new(false, false, false, true, Motivo: "El usuario está deshabilitado.");

    public static ResultadoLogin BloqueadoPor(TimeSpan? restante)
        => new(false, false, true, false, restante,
            "Demasiados intentos fallidos. Probá de nuevo más tarde.");
}

/// <summary>
/// Abstrae el inicio de sesión para que cambiar de Identity local a Keycloak
/// (F-Shell-5) sea sólo cambiar la implementación.
/// </summary>
public interface IProveedorAutenticacion
{
    Task<ResultadoLogin> IniciarSesionAsync(
        string usuario, string clave, bool recordarme, CancellationToken cancellationToken = default);

    Task<ResultadoLogin> VerificarSegundoFactorAsync(
        string codigoTotp, bool recordarDispositivo, CancellationToken cancellationToken = default);

    Task CerrarSesionAsync();
}
