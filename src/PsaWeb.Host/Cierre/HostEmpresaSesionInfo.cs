using PsaWeb.Modules.Reportes.Comun;
using PsaWeb.Seguridad;

namespace PsaWeb.Host.Cierre;

/// <summary>Nombre de la empresa de sesión para los reportes (implementación con shell).</summary>
public sealed class HostEmpresaSesionInfo(EmpresaActualService empresaActual) : IEmpresaSesionInfo
{
    public string? Nombre => empresaActual.Nombre;
}
