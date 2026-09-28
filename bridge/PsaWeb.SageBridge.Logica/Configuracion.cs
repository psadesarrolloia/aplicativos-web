using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using PsaWeb.SageBridge.Contratos;

namespace PsaWeb.SageBridge.Logica;

/// <summary>
/// Configuración del Bridge: archivo <c>PsaWeb.SageBridge.config.json</c> junto al .exe. Las cadenas de conexión
/// usan seguridad integrada (la cuenta del servicio); la clave de aplicación de Sage va cifrada con DPAPI
/// (<c>--proteger-clave</c>). Nunca se guardan contraseñas en claro.
/// </summary>
[DataContract]
public sealed class Configuracion
{
    public const string NombreArchivo = "PsaWeb.SageBridge.config.json";

    /// <summary>Nombre de esta instancia (se antepone el nombre del equipo en el latido).</summary>
    [DataMember] public string Instancia { get; set; } = "principal";

    /// <summary>Cadena a <c>PsaWebPlataforma</c> (cola, latido, empresas).</summary>
    [DataMember] public string PlataformaConnectionString { get; set; } = string.Empty;

    /// <summary>Cadena a <c>PeachEBills</c> (tabla <c>PeachConnString</c>: servidor y base de cada RUC).</summary>
    [DataMember] public string PeachEbillsConnectionString { get; set; } = string.Empty;

    /// <summary>Clave de aplicación de Sage (<c>thirdPartyApplicationKey</c>) cifrada con DPAPI del equipo.</summary>
    [DataMember(EmitDefaultValue = false)] public string? ClaveAplicacionProtegida { get; set; }

    /// <summary>
    /// SOLO DESARROLLO: ruta al <c>Secrets.json</c> del exe antiguo, de donde se lee la clave en tiempo de ejecución.
    /// </summary>
    [DataMember(EmitDefaultValue = false)] public string? ClaveAplicacionArchivo { get; set; }

    /// <summary>Ventana de mantenimiento para las empresas sin ventana propia (<c>HH:mm-HH:mm</c>).</summary>
    [DataMember] public string VentanaPorDefecto { get; set; } = "22:00-06:00";

    [DataMember] public int IntervaloSondeoSegundos { get; set; } = 10;

    /// <summary>Plazo del lease de cada trabajo tomado (se renueva antes de cada trabajo del lote).</summary>
    [DataMember] public int LeaseMinutos { get; set; } = 15;

    [DataMember] public int MaximoTrabajosPorLote { get; set; } = 20;

    /// <summary>
    /// Conversión OC → compra (F6, reemplazo del worker COM): cada cuántos minutos se programa por empresa habilitada, fuera de
    /// su ventana. 0 = no se programa (solo la encadenada al guardar una OC y la pedida a mano).
    /// </summary>
    [DataMember] public int ConversionMinutos { get; set; } = 5;

    /// <summary>Tras guardar una OC, encolar su conversión (el worker lo hacía en ~30 s).</summary>
    [DataMember] public bool ConvertirAlGuardar { get; set; } = true;

    /// <summary>Antigüedad máxima de las OC a convertir (el worker: <c>LimithMonths</c>).</summary>
    [DataMember] public int ConversionMesesAtras { get; set; } = 12;

    /// <summary>SOLO DESARROLLO: nombre del servidor de Sage a usar en vez del de <c>PeachConnString</c>.</summary>
    [DataMember(EmitDefaultValue = false)] public string? ServidorSageOverride { get; set; }

    /// <summary>SOLO DESARROLLO: <c>"RUC=base"</c> para apuntar un RUC a otra base (p. ej. una copia de prueba).</summary>
    [DataMember(EmitDefaultValue = false)] public string[]? BasesPorRuc { get; set; }

    /// <summary>
    /// Candado de seguridad: si tiene valores, el Bridge SOLO abre esas bases (en desarrollo: la copia de prueba).
    /// Vacío = sin restricción (producción).
    /// </summary>
    [DataMember(EmitDefaultValue = false)] public string[]? SoloBases { get; set; }

    [OnDeserializing]
    private void ValoresPorDefecto(StreamingContext _)
    {
        // DataContractJsonSerializer no ejecuta los inicializadores: los valores por defecto se ponen acá.
        Instancia = "principal";
        PlataformaConnectionString = string.Empty;
        PeachEbillsConnectionString = string.Empty;
        VentanaPorDefecto = "22:00-06:00";
        IntervaloSondeoSegundos = 10;
        LeaseMinutos = 15;
        MaximoTrabajosPorLote = 20;
        ConversionMinutos = 5;
        ConvertirAlGuardar = true;
        ConversionMesesAtras = 12;
    }

    public static Configuracion Cargar(string dirBase)
    {
        var ruta = Path.Combine(dirBase, NombreArchivo);
        if (!File.Exists(ruta))
        {
            throw new InvalidOperationException($"Falta el archivo de configuración {ruta}.");
        }

        var cfg = Json.Leer<Configuracion>(File.ReadAllText(ruta));
        cfg.Validar();
        return cfg;
    }

    public void Validar()
    {
        var errores = new List<string>();
        if (string.IsNullOrWhiteSpace(PlataformaConnectionString)) errores.Add("PlataformaConnectionString vacío.");
        if (string.IsNullOrWhiteSpace(PeachEbillsConnectionString)) errores.Add("PeachEbillsConnectionString vacío.");
        if (string.IsNullOrWhiteSpace(ClaveAplicacionProtegida) && string.IsNullOrWhiteSpace(ClaveAplicacionArchivo))
            errores.Add("Falta la clave de aplicación de Sage (ClaveAplicacionProtegida; ver --proteger-clave).");
        if (!VentanaMantenimiento.TryParsear(VentanaPorDefecto, out _)) errores.Add($"VentanaPorDefecto inválida: {VentanaPorDefecto}.");
        if (IntervaloSondeoSegundos < 2 || LeaseMinutos < 2 || MaximoTrabajosPorLote < 1) errores.Add("Intervalos o tamaño de lote fuera de rango.");
        foreach (var par in BasesPorRuc ?? Array.Empty<string>())
        {
            if (par.Split('=').Length != 2) errores.Add($"BasesPorRuc mal formado: «{par}» (use RUC=base).");
        }

        if (errores.Count > 0)
        {
            throw new InvalidOperationException("Configuración inválida: " + string.Join(" ", errores));
        }
    }

    public VentanaMantenimiento Ventana(string? ventanaEmpresa) =>
        VentanaMantenimiento.Parsear(string.IsNullOrWhiteSpace(ventanaEmpresa) ? VentanaPorDefecto : ventanaEmpresa);

    /// <summary>Base a usar para un RUC: la de <c>BasesPorRuc</c> si hay override, si no la de PeachConnString.</summary>
    public string BaseParaRuc(string ruc, string baseDePeachConnString)
    {
        foreach (var par in BasesPorRuc ?? Array.Empty<string>())
        {
            var partes = par.Split('=');
            if (partes[0].Trim() == ruc)
            {
                return partes[1].Trim();
            }
        }

        return baseDePeachConnString;
    }

    public bool BasePermitida(string baseDatos) =>
        SoloBases is not { Length: > 0 } || SoloBases.Any(b => string.Equals(b.Trim(), baseDatos, StringComparison.OrdinalIgnoreCase));

    /// <summary>Devuelve la clave de aplicación de Sage (descifrada). Nunca se escribe en el log.</summary>
    public string ClaveAplicacion()
    {
        if (!string.IsNullOrWhiteSpace(ClaveAplicacionProtegida))
        {
            return Secretos.Desproteger(ClaveAplicacionProtegida!);
        }

        var json = File.ReadAllText(ClaveAplicacionArchivo!);
        var m = Regex.Match(json, "\"thirdPartyApplicationKey\"\\s*:\\s*\"([^\"]+)\"");
        if (!m.Success)
        {
            throw new InvalidOperationException("No se encontró thirdPartyApplicationKey en " + ClaveAplicacionArchivo + ".");
        }

        return m.Groups[1].Value;
    }
}
