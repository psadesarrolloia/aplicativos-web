using System;
using System.Diagnostics;
using System.Threading;

namespace PsaWeb.SageBridge;

/// <summary>
/// Lanza y vigila al proceso trabajador (este mismo .exe con <c>--trabajador</c>: así el proceso que usa el SDK es
/// el ejecutable autorizado en Sage). Si el trabajador sale con <see cref="Programa.CodigoReciclar"/> lo relanza al
/// instante; si se cae, espera cada vez más (5 s … 2 min) para no martillar.
/// </summary>
internal sealed class Supervisor
{
    private static readonly TimeSpan[] Esperas =
    {
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2),
    };

    private readonly Bitacora _bitacora;
    private readonly bool _eco;
    private readonly ManualResetEvent _detenerSupervisor = new ManualResetEvent(false);
    private readonly EventWaitHandle _detenerTrabajador;
    private Thread? _hilo;
    private Process? _trabajador;

    public Supervisor(Bitacora bitacora, bool eco)
    {
        _bitacora = bitacora;
        _eco = eco;
        _detenerTrabajador = new EventWaitHandle(false, EventResetMode.ManualReset,
            NombreEventoDetener(Process.GetCurrentProcess().Id));
    }

    /// <summary>Evento con nombre por el que el supervisor pide al trabajador que termine su lote y salga.</summary>
    public static string NombreEventoDetener(int pidSupervisor) => $"PsaWeb.SageBridge.Detener.{pidSupervisor}";

    public void Iniciar()
    {
        Log($"Supervisor iniciado (PID {Process.GetCurrentProcess().Id}, usuario {Environment.UserDomainName}\\{Environment.UserName}).");
        _hilo = new Thread(Ciclo) { IsBackground = true, Name = "Supervisor" };
        _hilo.Start();
    }

    public void Detener()
    {
        Log("Deteniendo: se pide al trabajador que termine el lote en curso.");
        _detenerSupervisor.Set();
        _detenerTrabajador.Set();
        var t = _trabajador;
        if (t != null && !t.HasExited && !t.WaitForExit(90_000))
        {
            Log("El trabajador no terminó en 90 s: se lo detiene a la fuerza.");
            try { t.Kill(); } catch { }
        }

        _hilo?.Join(5_000);
        Log("Supervisor detenido.");
    }

    private void Ciclo()
    {
        var fallasSeguidas = 0;
        while (!_detenerSupervisor.WaitOne(0))
        {
            var inicio = DateTime.UtcNow;
            int codigo;
            try
            {
                codigo = EjecutarTrabajador();
            }
            catch (Exception ex)
            {
                Log("No se pudo lanzar el trabajador: " + ex.Message);
                codigo = -1;
            }

            if (_detenerSupervisor.WaitOne(0))
            {
                break;
            }

            if (codigo == Programa.CodigoReciclar)
            {
                Log("El trabajador pidió reciclarse (error de base del SDK): se relanza.");
                fallasSeguidas = 0;
                continue;
            }

            // Un trabajador que anduvo más de 10 minutos antes de caer no cuenta como falla seguida.
            fallasSeguidas = DateTime.UtcNow - inicio > TimeSpan.FromMinutes(10) ? 1 : fallasSeguidas + 1;
            var espera = Esperas[Math.Min(fallasSeguidas - 1, Esperas.Length - 1)];
            Log($"El trabajador terminó con código {codigo}; se relanza en {espera.TotalSeconds:0} s.");
            _detenerSupervisor.WaitOne(espera);
        }
    }

    private int EjecutarTrabajador()
    {
        var exe = Process.GetCurrentProcess().MainModule!.FileName;
        var info = new ProcessStartInfo(exe, $"{Programa.ArgumentoTrabajador} {Process.GetCurrentProcess().Id}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
        };
        using var p = Process.Start(info)!;
        _trabajador = p;
        Log($"Trabajador lanzado (PID {p.Id}).");
        p.WaitForExit();
        _trabajador = null;
        return p.ExitCode;
    }

    private void Log(string mensaje)
    {
        _bitacora.Escribir("[supervisor] " + mensaje);
        if (_eco)
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {mensaje}");
        }
    }
}
