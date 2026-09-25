using System.ServiceProcess;

namespace PsaWeb.SageBridge;

/// <summary>Servicio de Windows <c>PsaSageBridge</c>: solo arranca y detiene al <see cref="Supervisor"/>.</summary>
internal sealed class ServicioBridge : ServiceBase
{
    private readonly Bitacora _bitacora;
    private Supervisor? _supervisor;

    public ServicioBridge(Bitacora bitacora)
    {
        _bitacora = bitacora;
        ServiceName = Programa.NombreServicio;
        CanStop = true;
        CanShutdown = true;
    }

    protected override void OnStart(string[] args)
    {
        _supervisor = new Supervisor(_bitacora, eco: false);
        _supervisor.Iniciar();
    }

    protected override void OnStop()
    {
        // El trabajador puede tardar en cerrar la compañía; se le pide tiempo extra al SCM.
        RequestAdditionalTime(120_000);
        _supervisor?.Detener();
    }

    protected override void OnShutdown() => OnStop();
}
