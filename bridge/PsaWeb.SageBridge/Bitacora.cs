using System;
using System.IO;
using System.Text;

namespace PsaWeb.SageBridge;

/// <summary>Log diario en texto (<c>logs\&lt;prefijo&gt;-yyyyMMdd.log</c>, UTF-8). Nunca lanza.</summary>
internal sealed class Bitacora
{
    private readonly string _carpeta;
    private readonly string _prefijo;
    private readonly object _candado = new object();

    public Bitacora(string carpeta, string prefijo)
    {
        _carpeta = carpeta;
        _prefijo = prefijo;
    }

    public void Escribir(string mensaje)
    {
        try
        {
            lock (_candado)
            {
                Directory.CreateDirectory(_carpeta);
                var ruta = Path.Combine(_carpeta, $"{_prefijo}-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(ruta, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{System.Diagnostics.Process.GetCurrentProcess().Id}] {mensaje}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch
        {
            // Un log que falla no debe tumbar al Bridge.
        }
    }
}
