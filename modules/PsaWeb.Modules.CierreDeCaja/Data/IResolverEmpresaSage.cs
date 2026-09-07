namespace PsaWeb.Modules.CierreDeCaja.Data;

/// <summary>
/// Resuelve contra qué empresa de Sage 50 trabaja el módulo. Con el shell activo,
/// lo implementa el Host a partir de <c>EmpresaActualService</c> +
/// <c>PeachConnStringResolver</c>. Sin shell, se usa la implementación por defecto
/// (<see cref="SinShellResolverEmpresaSage"/>) y todo cae en la cadena de
/// <c>Sage50:ConnectionString</c>.
/// </summary>
public interface IResolverEmpresaSage
{
    /// <summary>RUC de la empresa de sesión, o <c>null</c> si no hay shell.</summary>
    string? RucSesion { get; }

    /// <summary>Se dispara cuando cambia la empresa de sesión.</summary>
    event Action? Cambio;

    /// <summary>
    /// Cadena de conexión ODBC de Sage 50 para <paramref name="ruc"/>.
    /// Devolver <c>null</c> significa «usar la cadena de configuración».
    /// </summary>
    Task<string?> CadenaOdbcAsync(string ruc, CancellationToken cancellationToken = default);
}

/// <summary>Implementación sin shell: siempre la cadena de configuración.</summary>
public sealed class SinShellResolverEmpresaSage : IResolverEmpresaSage
{
    public string? RucSesion => null;

    public event Action? Cambio { add { } remove { } }

    public Task<string?> CadenaOdbcAsync(string ruc, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}
