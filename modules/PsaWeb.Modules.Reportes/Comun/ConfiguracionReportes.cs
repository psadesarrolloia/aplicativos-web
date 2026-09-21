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

    /// <summary>
    /// Bug C1 corregido (decisión del usuario, 2026-09-21): abono = importe aplicado por el recibo, no el total pagado de la
    /// factura. Desmarcar reproduce el cálculo del reporte de Access.
    /// </summary>
    public bool AbonoPorRecibo { get; set; } = true;

    /// <summary>Bug C2 corregido (decisión del usuario, 2026-09-21): rango de recibos comparado como número, no como texto.</summary>
    public bool RangoNumerico { get; set; } = true;
}

/// <summary>Personalización del cheque + comprobante de egreso.</summary>
public sealed class ConfiguracionCheque
{
    /// <summary>Fuente de los campos. Arial como el reporte de Access (el texto se mide con las métricas reales de la fuente).</summary>
    public string Fuente { get; set; } = "Arial";
    public double Tamano { get; set; } = 10;

    /// <summary>«en» = 1,234.56 (como imprime hoy el reporte de Access en las PC de PSA, verificado en un escaneo) · «es» = 1.234,56.</summary>
    public string FormatoMonto { get; set; } = "en";

    /// <summary>
    /// Desplazamiento X/Y en mm sumado a TODAS las medidas (ajuste por empresa/impresora/cheque). Las medidas del reporte de
    /// Access se cuentan desde el MARGEN del reporte, no desde la esquina de la hoja: 10,0 mm a la izquierda y 13,0 mm arriba
    /// (confirmado midiendo un cheque impreso). Poner 0 / 0 para contarlas desde la esquina de la hoja.
    /// </summary>
    public double CorreccionX { get; set; } = MargenAccessX;
    public double CorreccionY { get; set; } = MargenAccessY;

    public const double MargenAccessX = 10.0;
    public const double MargenAccessY = 13.0;

    /// <summary>Bug Q1 corregido (decisión del usuario, 2026-09-21): «CON xx/100» y relleno también por debajo de $2,00.</summary>
    public bool CorregirMontosMenoresA2 { get; set; } = true;

    /// <summary>Encabezados de las 2 columnas de importes del comprobante (el escaneo de Demoradio dice DEBITO/CREDITO; el reporte de Efemedio, PAGOS/CHEQUE).</summary>
    public string EncabezadoDebito { get; set; } = "DEBITO";
    public string EncabezadoCredito { get; set; } = "CREDITO";

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
