using PsaWeb.PeachEbills;
using PsaWeb.Sage50;
using PsaWeb.Seguridad;

namespace PsaWeb.Host.Cierre;

/// <summary>
/// Implementación con shell de <see cref="IResolverEmpresaSage"/>: la empresa es
/// la de <see cref="EmpresaActualService"/> y la cadena ODBC se arma por RUC con
/// <see cref="PeachConnStringResolver"/> (tabla <c>PeachConnString</c>).
/// </summary>
public sealed class HostResolverEmpresaSage : IResolverEmpresaSage
{
    private readonly EmpresaActualService _empresaActual;
    private readonly PeachConnStringResolver _resolver;

    public HostResolverEmpresaSage(EmpresaActualService empresaActual, PeachConnStringResolver resolver)
    {
        _empresaActual = empresaActual;
        _resolver = resolver;
    }

    public string? RucSesion => _empresaActual.Ruc;

    public event Action? Cambio
    {
        add => _empresaActual.Cambio += value;
        remove => _empresaActual.Cambio -= value;
    }

    public async Task<string?> CadenaOdbcAsync(string ruc, CancellationToken cancellationToken = default)
        => await _resolver.ResolverCadenaOdbcAsync(ruc, cancellationToken);
}
