namespace PsaWeb.Seguridad;

public enum Ambiente : short
{
    Pruebas = 1,
    Produccion = 2,
}

/// <summary>
/// Estado de sesión (circuito Blazor): empresa y ambiente elegidos por el usuario
/// tras el login. Lo consumen los aplicativos para saber contra qué empresa
/// trabajar. <c>Scoped</c>.
/// </summary>
public sealed class EmpresaActualService
{
    private EmpresaDelUsuario? _empresa;

    public EmpresaDelUsuario? Empresa => _empresa;
    public Ambiente Ambiente { get; private set; } = Ambiente.Produccion;

    public bool HaySeleccion => _empresa is not null;
    public string? Ruc => _empresa?.Ruc;
    public string? Nombre => _empresa?.Nombre;

    /// <summary>Se dispara cuando cambia la empresa o el ambiente.</summary>
    public event Action? Cambio;

    public void Fijar(EmpresaDelUsuario empresa, Ambiente ambiente)
    {
        _empresa = empresa;
        Ambiente = ambiente;
        Cambio?.Invoke();
    }

    public void CambiarAmbiente(Ambiente ambiente)
    {
        if (Ambiente == ambiente) return;
        Ambiente = ambiente;
        Cambio?.Invoke();
    }

    public void Limpiar()
    {
        _empresa = null;
        Ambiente = Ambiente.Produccion;
        Cambio?.Invoke();
    }
}
