namespace PsaWeb.Datil.Model;

/// <summary>
/// Nota de crédito de venta para el API de Datil.
/// Port de <c>DatilClientLibrary.NotaDeCredito</c>. Se serializa a
/// <c>snake_case</c> sin nulos (ver <see cref="DatilJson"/>).
/// </summary>
public sealed class NotaCredito
{
    public string Secuencial { get; set; } = string.Empty;

    public string Moneda { get; set; } = "USD";

    public DateTimeOffset FechaEmision { get; set; }

    /// <summary>Fecha de emisión del documento que se modifica.</summary>
    public DateTimeOffset FechaEmisionDocumentoModificado { get; set; }

    /// <summary>Número del documento modificado (<c>001-001-000000123</c>).</summary>
    public string NumeroDocumentoModificado { get; set; } = string.Empty;

    /// <summary>Tipo del documento modificado (01 factura, …).</summary>
    public string TipoDocumentoModificado { get; set; } = string.Empty;

    public string Motivo { get; set; } = string.Empty;

    /// <summary>1 = pruebas, 2 = producción.</summary>
    public int Ambiente { get; set; } = 1;

    /// <summary>1 = emisión normal, 2 = por indisponibilidad.</summary>
    public int TipoEmision { get; set; } = 1;

    public string Version { get; set; } = "1.0.0";

    /// <summary>Si es null, Datil la genera.</summary>
    public string? ClaveAcceso { get; set; }

    public Emisor Emisor { get; set; } = new();

    public Comprador Comprador { get; set; } = new();

    public List<ItemComprobante> Items { get; set; } = new();

    public TotalesNotaCredito Totales { get; set; } = new();

    /// <summary>Información adicional como diccionario (claves tal cual).</summary>
    public Dictionary<string, string>? InformacionAdicional { get; set; }
}

/// <summary>Totales de una nota de crédito. Port de <c>DatilClientLibrary.TotalesNotaDeCredito</c>.</summary>
public sealed class TotalesNotaCredito
{
    public double TotalSinImpuestos { get; set; }
    public double ImporteTotal { get; set; }

    /// <summary>Impuestos de totales (sin tarifa).</summary>
    public List<Impuesto> Impuestos { get; set; } = new();
}
