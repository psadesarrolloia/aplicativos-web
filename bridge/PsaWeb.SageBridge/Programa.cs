using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;

namespace PsaWeb.SageBridge;

/// <summary>
/// Modos de ejecución:
/// <list type="bullet">
/// <item>sin argumentos → servicio de Windows (supervisor);</item>
/// <item><c>--consola</c> → supervisor en consola (desarrollo; Ctrl+C para detener);</item>
/// <item><c>--trabajador &lt;pid&gt;</c> → proceso trabajador (lo lanza el supervisor; usa el SDK);</item>
/// <item>cualquier otro <c>--comando</c> → lo resuelve la lógica (p. ej. <c>--proteger-clave</c>, <c>--ayuda</c>).</item>
/// </list>
/// </summary>
internal static class Programa
{
    internal const string NombreServicio = "PsaSageBridge";
    internal const string ArgumentoTrabajador = "--trabajador";

    /// <summary>Código de salida con el que el trabajador pide que lo reinicien de inmediato (proceso envenenado).</summary>
    internal const int CodigoReciclar = 3;

    [STAThread]
    private static int Main(string[] args)
    {
        var dirBase = AppDomain.CurrentDomain.BaseDirectory;
        var bitacora = new Bitacora(Path.Combine(dirBase, "logs"), "anfitrion");

        if (args.Length == 0)
        {
            ServiceBase.Run(new ServicioBridge(bitacora));
            return 0;
        }

        switch (args[0])
        {
            case "--consola":
                return EjecutarEnConsola(bitacora);
            case ArgumentoTrabajador:
                return EjecutarTrabajador(dirBase, args, bitacora);
            default:
                return InvocarLogica("Comando", new object[] { dirBase, args, (Action<string>)Console.WriteLine }, bitacora);
        }
    }

    private static int EjecutarEnConsola(Bitacora bitacora)
    {
        var supervisor = new Supervisor(bitacora, eco: true);
        var fin = new ManualResetEvent(false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; fin.Set(); };
        supervisor.Iniciar();
        Console.WriteLine("Sage Bridge en consola. Ctrl+C para detener.");
        fin.WaitOne();
        supervisor.Detener();
        return 0;
    }

    private static int EjecutarTrabajador(string dirBase, string[] args, Bitacora bitacora)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var pidPadre))
        {
            bitacora.Escribir("Trabajador: falta el PID del supervisor.");
            return 2;
        }

        // El supervisor pide detenerse señalando este evento; si el supervisor muere, también se detiene.
        var detener = new EventWaitHandle(false, EventResetMode.ManualReset, Supervisor.NombreEventoDetener(pidPadre));
        var vigia = new Thread(() =>
        {
            try { Process.GetProcessById(pidPadre).WaitForExit(); } catch { }
            detener.Set();
        }) { IsBackground = true };
        vigia.Start();

        Action<string> log = mensaje => bitacora.Escribir("[trabajador] " + mensaje);
        return InvocarLogica("Trabajar", new object[] { dirBase, detener, log }, bitacora);
    }

    /// <summary>Carga PsaWeb.SageBridge.Logica.dll e invoca <c>PuntoEntrada.&lt;metodo&gt;</c> (devuelve el código de salida).</summary>
    private static int InvocarLogica(string metodo, object[] argumentos, Bitacora bitacora)
    {
        try
        {
            var dll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PsaWeb.SageBridge.Logica.dll");
            var tipo = Assembly.LoadFrom(dll).GetType("PsaWeb.SageBridge.Logica.PuntoEntrada", throwOnError: true)!;
            return (int)tipo.GetMethod(metodo, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, argumentos)!;
        }
        catch (Exception ex)
        {
            var real = ex is TargetInvocationException { InnerException: not null } tie ? tie.InnerException! : ex;
            bitacora.Escribir($"Error al ejecutar la lógica ({metodo}): {real}");
            Console.Error.WriteLine(real.Message);
            return 2;
        }
    }
}
