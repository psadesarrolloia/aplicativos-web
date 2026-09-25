using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PsaWeb.SageBridge.Logica;

/// <summary>
/// JSON con el serializador del propio .NET Framework (sin paquetes). Los resultados son objetos planos
/// (texto, números, booleanos): el Host los lee con System.Text.Json sin problema. No usar fechas en contratos.
/// </summary>
internal static class Json
{
    public static string Escribir<T>(T valor)
    {
        using var ms = new MemoryStream();
        new DataContractJsonSerializer(typeof(T)).WriteObject(ms, valor);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static T Leer<T>(string json)
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(ms)!;
    }
}
