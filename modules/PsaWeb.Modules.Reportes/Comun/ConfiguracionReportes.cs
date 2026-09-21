using System.Text.Json;

namespace PsaWeb.Modules.Reportes.Comun;

/// <summary>Claves de la tabla de configuración (una fila por empresa + reporte).</summary>
public static class ClavesReporte
{
    public const string Empresa = "empresa";
    public const string Pwc = "pwc";
    public const string Comisiones = "comisiones";
    public const string Cheques = "cheques";
}

/// <summary>Datos de la empresa que comparten los 3 reportes (nada fijo a Efemedio).</summary>
public sealed class ConfiguracionEmpresa
{
    /// <summary>Nombre a imprimir; vacío = el de la empresa de sesión.</summary>
    public string? NombreEmpresa { get; set; }

    /// <summary>Ciudad del «ciudad, fecha» del cheque (en Access estaba fija en «Quito»).</summary>
    public string Ciudad { get; set; } = "";
}

/// <summary>Personalización del reporte PWC (cuentas por cobrar).</summary>
public sealed class ConfiguracionPwc
{
    /// <summary>Rótulo de la columna A (en Access «RADIO (RAZON SOCIAL)»).</summary>
    public string EncabezadoRazonSocial { get; set; } = "RADIO (RAZON SOCIAL)";

    /// <summary>Quien cobra: «PWC» → «MONTO A COBRAR POR PWC».</summary>
    public string Cobrador { get; set; } = "PWC";

    /// <summary>Título del archivo; vacío = «CXC {cobrador}».</summary>
    public string? Titulo { get; set; }

    public bool MostrarEmpresa { get; set; } = true;
    public bool MostrarOrden { get; set; } = true;
    public bool MostrarCiudad { get; set; } = true;
    public bool MostrarAnunciante { get; set; } = true;
    public bool MostrarRetenciones { get; set; } = true;
    public bool MostrarDireccion { get; set; }
}

/// <summary>Personalización del reporte de Comisiones.</summary>
public sealed class ConfiguracionComisiones
{
    public string? Titulo { get; set; }
    public bool MostrarCiudad { get; set; }

    /// <summary>Columna de control: importe que aplicó ese recibo a la factura (según Sage).</summary>
    public bool MostrarImporteRecibo { get; set; }

    /// <summary>Bug C1: abono = importe aplicado por el recibo (no el total pagado de la factura).</summary>
    public bool AbonoPorRecibo { get; set; }

    /// <summary>Bug C2: comparar el rango de recibos como número (no como texto).</summary>
    public bool RangoNumerico { get; set; }
}

/// <summary>Personalización del cheque + comprobante de egreso.</summary>
public sealed class ConfiguracionCheque
{
    /// <summary>Fuente de los campos; monoespaciada por defecto (Courier).</summary>
    public string Fuente { get; set; } = "Courier New";
    public double Tamano { get; set; } = 10;

    /// <summary>«es» = 1.234,56 (Windows es-EC) · «en» = 1,234.56.</summary>
    public string FormatoMonto { get; set; } = "es";

    /// <summary>Corrección X/Y en mm sumada a todas las coordenadas (calibración de impresión).</summary>
    public double CorreccionX { get; set; }
    public double CorreccionY { get; set; }

    /// <summary>Bug Q1: agregar «CON xx/100» también a los montos menores a $2,00.</summary>
    public bool CorregirMontosMenoresA2 { get; set; }

    public bool ImprimirRotuloNumero { get; set; } = true;
    public string RotuloNumero { get; set; } = "CH.No.:";
    public string Firma1 { get; set; } = "RECIBIDO POR";
    public string Firma2 { get; set; } = "ELABORADO POR";
    public string Firma3 { get; set; } = "REVISADO POR";
}

/// <summary>Logo de la empresa (PNG/JPG) para el comprobante de egreso.</summary>
public sealed record LogoEmpresa(byte[] Contenido, string TipoMime);

/// <summary>
/// Configuración de los reportes por empresa (RUC). Vive en <c>PsaWebPlataforma</c>
/// (nunca en <c>PeachEBills</c>, que es compartida); sin plataforma configurada (dev)
/// queda en memoria.
/// </summary>
public interface IServicioConfiguracionReportes
{
    Task<T> ObtenerAsync<T>(string ruc, string reporte, CancellationToken cancellationToken = default)
        where T : class, new();

    Task GuardarAsync<T>(string ruc, string reporte, T valor, string? usuario, CancellationToken cancellationToken = default)
        where T : class;

    Task<LogoEmpresa?> LogoAsync(string ruc, CancellationToken cancellationToken = default);

    Task GuardarLogoAsync(string ruc, LogoEmpresa? logo, string? usuario, CancellationToken cancellationToken = default);
}

/// <summary>
/// Nombre de la empresa de sesión (para el nombre por defecto de los encabezados). El Host lo
/// implementa con <c>EmpresaActualService</c>; por defecto no hay nombre.
/// </summary>
public interface IEmpresaSesionInfo
{
    string? Nombre { get; }
}

internal sealed class SinEmpresaSesionInfo : IEmpresaSesionInfo
{
    public string? Nombre => null;
}

internal static class JsonConfig
{
    internal static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    internal static T Leer<T>(string? json) where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new T();
        }
        try
        {
            return JsonSerializer.Deserialize<T>(json, Opciones) ?? new T();
        }
        catch (JsonException)
        {
            // Una fila corrupta no debe tirar la página: vuelve a los valores por defecto.
            return new T();
        }
    }

    internal static string Escribir<T>(T valor) => JsonSerializer.Serialize(valor, Opciones);
}
