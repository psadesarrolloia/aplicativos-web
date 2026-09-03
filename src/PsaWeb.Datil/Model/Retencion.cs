namespace PsaWeb.Datil.Model;

/// <summary>
/// Comprobante de retención para el API de Datil. Las propiedades se serializan
/// a <c>snake_case</c> (ver <see cref="DatilJson"/>).
/// </summary>
public sealed class Retencion
{
    /// <summary>Número de secuencia.</summary>
    public string Secuencial { get; set; } = string.Empty;

    public DateTimeOffset FechaEmision { get; set; }

    /// <summary>1 = pruebas, 2 = producción.</summary>
    public int Ambiente { get; set; } = 1;

    /// <summary>1 = emisión normal, 2 = por indisponibilidad.</summary>
    public int TipoEmision { get; set; } = 1;

    public string Version { get; set; } = "1.0.0";

    /// <summary>Si es null, Datil la genera.</summary>
    public string? ClaveAcceso { get; set; }

    /// <summary>Periodo fiscal MM/AAAA.</summary>
    public string PeriodoFiscal { get; set; } = string.Empty;

    public Emisor Emisor { get; set; } = new();

    public Comprador Sujeto { get; set; } = new();

    public List<ItemRetencion> Items { get; set; } = new();

    public Dictionary<string, string>? InformacionAdicional { get; set; }
}

public sealed class Emisor
{
    public string Ruc { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string NombreComercial { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;

    /// <summary>Número de resolución; vacío si no es contribuyente especial.</summary>
    public string ContribuyenteEspecial { get; set; } = string.Empty;

    public bool ObligadoContabilidad { get; set; }

    public Establecimiento Establecimiento { get; set; } = new();
}

public sealed class Establecimiento
{
    /// <summary>Código de 3 dígitos. Ej: 001</summary>
    public string Codigo { get; set; } = string.Empty;

    /// <summary>Punto de emisión de 3 dígitos. Ej: 001</summary>
    public string PuntoEmision { get; set; } = string.Empty;

    public string Direccion { get; set; } = string.Empty;
}

public sealed class Comprador
{
    public string RazonSocial { get; set; } = string.Empty;

    /// <summary>Identificación (RUC, cédula, pasaporte…), 5–20 caracteres.</summary>
    public string Identificacion { get; set; } = string.Empty;

    /// <summary>04 RUC · 05 cédula · 06 pasaporte · 07 consumidor final · 08 exterior · 09 placa.</summary>
    public string TipoIdentificacion { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Direccion { get; set; } = string.Empty;

    public string? Telefono { get; set; }
}

public sealed class ItemRetencion
{
    public string Codigo { get; set; } = string.Empty;
    public string CodigoPorcentaje { get; set; } = string.Empty;
    public double Porcentaje { get; set; }
    public double BaseImponible { get; set; }
    public double ValorRetenido { get; set; }
    public DateTimeOffset FechaEmisionDocumentoSustento { get; set; }

    /// <summary>Número del documento sustento: 001-002-000000003.</summary>
    public string NumeroDocumentoSustento { get; set; } = string.Empty;

    public string TipoDocumentoSustento { get; set; } = string.Empty;
}
