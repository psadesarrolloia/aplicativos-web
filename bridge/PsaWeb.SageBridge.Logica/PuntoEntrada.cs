using System;
using System.Text;
using System.Threading;

namespace PsaWeb.SageBridge.Logica;

/// <summary>
/// Lo que invoca el anfitrión (<c>PsaWeb.SageBridge.exe</c>) por reflexión. Mantener estas firmas estables:
/// el anfitrión no se recompila cuando cambia la lógica.
/// </summary>
public static class PuntoEntrada
{
    /// <summary>Proceso trabajador. Devuelve 0 al detenerse, 3 para pedir que lo reciclen, 2 si la configuración es inválida.</summary>
    public static int Trabajar(string dirBase, WaitHandle detener, Action<string> log)
    {
        Configuracion cfg;
        try
        {
            cfg = Configuracion.Cargar(dirBase);
        }
        catch (Exception ex)
        {
            log(ex.Message);
            return 2;
        }

        return new Trabajador(cfg, log).Ejecutar(detener);
    }

    /// <summary>Comandos de administración (<c>--proteger-clave</c>, <c>--probar-config</c>, <c>--ayuda</c>).</summary>
    public static int Comando(string dirBase, string[] args, Action<string> salida)
    {
        switch (args[0])
        {
            case "--proteger-clave":
                return ProtegerClave(salida);
            case "--probar-config":
                return ProbarConfig(dirBase, salida);
            default:
                salida("""
                    PSA Sage Bridge
                      (sin argumentos)   servicio de Windows «PsaSageBridge»
                      --consola          supervisor en consola (desarrollo; Ctrl+C detiene)
                      --probar-config    valida la configuración y las conexiones SQL (no abre Sage)
                      --proteger-clave   cifra la clave de aplicación de Sage para este equipo (DPAPI)
                    """);
                return args[0] == "--ayuda" ? 0 : 1;
        }
    }

    private static int ProtegerClave(Action<string> salida)
    {
        Console.Write("Clave de aplicación de Sage (thirdPartyApplicationKey), no se muestra: ");
        var sb = new StringBuilder();
        ConsoleKeyInfo k;
        while ((k = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (k.Key == ConsoleKey.Backspace && sb.Length > 0) sb.Length--;
            else if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
        }

        Console.WriteLine();
        if (sb.Length == 0)
        {
            salida("No se ingresó ninguna clave.");
            return 1;
        }

        salida("Copie este valor en «ClaveAplicacionProtegida» de " + Configuracion.NombreArchivo + " (solo sirve en este equipo):");
        salida(Secretos.Proteger(sb.ToString()));
        return 0;
    }

    private static int ProbarConfig(string dirBase, Action<string> salida)
    {
        try
        {
            var cfg = Configuracion.Cargar(dirBase);
            salida($"Configuración válida. Instancia {Environment.MachineName}/{cfg.Instancia}; ventana por defecto {cfg.VentanaPorDefecto}; " +
                   $"SoloBases: {(cfg.SoloBases is { Length: > 0 } ? string.Join(", ", cfg.SoloBases) : "(sin restricción)")}.");
            new ColaSql(cfg.PlataformaConnectionString).ProbarConexion();
            salida("PsaWebPlataforma: conexión OK (tabla TrabajosSage presente).");
            using (var cn = new System.Data.SqlClient.SqlConnection(cfg.PeachEbillsConnectionString))
            {
                cn.Open();
            }

            salida("PeachEBills: conexión OK.");
            salida(string.IsNullOrEmpty(cfg.ClaveAplicacion()) ? "Clave de aplicación: VACÍA." : "Clave de aplicación: presente (no se muestra).");
            return 0;
        }
        catch (Exception ex)
        {
            salida("ERROR: " + ex.Message);
            return 1;
        }
    }
}
