using System.Globalization;
using System.Text.RegularExpressions;

namespace PsaWeb.Modules.Reportes.Pwc;

public enum TipoRetencion
{
    /// <summary>Retención en la fuente del impuesto a la renta (<c>Jobs.JobID = 'IRF'</c>).</summary>
    Renta,

    /// <summary>Retención del IVA (<c>Jobs.JobID = 'IVA'</c>).</summary>
    Iva,
}

/// <summary>Factura de venta con saldo tal como sale de Sage (una fila de la consulta de cabecera).</summary>
public sealed record CabeceraFacturaPwc(
    long PostOrder,
    string Cliente,
    string Factura,
    DateOnly Emision,
    DateOnly? Vence,
    decimal? Total,
    decimal? Pagado,
    string Anunciante,
    string Direccion,
    string Orden,
    string Ciudad);

/// <summary>Suma de <c>JrnlRow.Amount</c> de una factura por <c>RowType</c> + <c>TaxAuthorityCode</c>.</summary>
public sealed record GrupoImpuestoCrudo(long PostOrder, long RowType, string TaxAuthorityCode, decimal Subt);

/// <summary>Línea de una nota de crédito «4R» enlazada a la factura (retención).</summary>
public sealed record RetencionCruda(long FacturaPostOrder, string JobId, decimal Monto, string Descripcion);

public sealed record RetencionPwc(TipoRetencion Tipo, decimal Monto, decimal? Porcentaje, string Descripcion);

/// <summary>Una fila del reporte PWC (una factura con saldo).</summary>
public sealed record FilaPwc(
    long PostOrder,
    string Factura,
    DateOnly Emision,
    DateOnly? Vence,
    string Cliente,
    string Orden,
    string Ciudad,
    string Anunciante,
    string Direccion,
    decimal Subtotal,
    decimal Iva,
    decimal Total,
    decimal RetRenta,
    decimal RetIva,
    decimal MontoCobrar,
    RetencionPwc? RetencionRentaGanadora,
    RetencionPwc? RetencionIvaGanadora)
{
    /// <summary>
    /// Columna N del Excel («DESCUENTOS (N/C, COMISIONES)»): la fórmula de Access era <c>K−L−M−O</c>
    /// (total − ret. IR − ret. IVA − monto a cobrar).
    /// </summary>
    public decimal Descuentos => Total - RetRenta - RetIva - MontoCobrar;
}

/// <summary>Filtros del reporte. Todo opcional: vacío = todo, como el reporte de Access.</summary>
public sealed record FiltroPwc(
    DateOnly? EmisionDesde = null,
    DateOnly? EmisionHasta = null,
    DateOnly? VenceDesde = null,
    DateOnly? VenceHasta = null,
    string? Cliente = null,
    string? Factura = null,
    string? Anunciante = null,
    IReadOnlyList<string>? Ciudades = null)
{
    /// <summary>Valor con el que se filtra «(sin ciudad)».</summary>
    public const string SinCiudad = "";

    public bool RangoEmisionValido => EmisionDesde is null || EmisionHasta is null || EmisionDesde <= EmisionHasta;
    public bool RangoVenceValido => VenceDesde is null || VenceHasta is null || VenceDesde <= VenceHasta;

    public bool Coincide(CabeceraFacturaPwc f)
    {
        if (EmisionDesde is { } ed && f.Emision < ed) return false;
        if (EmisionHasta is { } eh && f.Emision > eh) return false;
        if (VenceDesde is { } vd && (f.Vence is null || f.Vence < vd)) return false;
        if (VenceHasta is { } vh && (f.Vence is null || f.Vence > vh)) return false;
        if (!Contiene(f.Cliente, Cliente)) return false;
        if (!Contiene(f.Factura, Factura)) return false;
        if (!Contiene(f.Anunciante, Anunciante)) return false;
        if (Ciudades is { Count: > 0 } ciudades
            && !ciudades.Any(c => string.Equals(c ?? "", f.Ciudad, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        return true;
    }

    private static bool Contiene(string valor, string? buscado)
        => string.IsNullOrWhiteSpace(buscado)
           || CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                  valor, buscado.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
}

/// <summary>Valores presentes en los datos, para llenar los selectores de filtro.</summary>
public sealed record OpcionesPwc(IReadOnlyList<string> Ciudades, IReadOnlyList<string> Clientes)
{
    public static readonly OpcionesPwc Vacias = new(Array.Empty<string>(), Array.Empty<string>());
}

/// <summary>Total de retenciones por tipo y porcentaje (el resumen dinámico del encabezado).</summary>
public sealed record ResumenRetencionPwc(TipoRetencion Tipo, decimal? Porcentaje, decimal Total, int Facturas)
{
    public string Etiqueta => Tipo switch
    {
        TipoRetencion.Renta => Porcentaje is { } p ? $"RET. IR {Formato(p)}%" : "RET. IR (sin %)",
        _ => Porcentaje is { } q ? $"RET. IVA {Formato(q)}%" : "RET. IVA (sin %)",
    };

    /// <summary>Rótulo corto para la columna auxiliar del Excel.</summary>
    public string EtiquetaColumna => Tipo switch
    {
        TipoRetencion.Renta => Porcentaje is { } p ? $"RF {Formato(p)}%" : "RF s/%",
        _ => Porcentaje is { } q ? $"IVA {Formato(q)}%" : "IVA s/%",
    };

    private static string Formato(decimal valor)
        => valor.ToString("0.##", CultureInfo.GetCultureInfo("es-EC"));
}

public sealed record ResumenCiudadPwc(string Ciudad, int Facturas, decimal MontoCobrar)
{
    public string Etiqueta => string.IsNullOrEmpty(Ciudad) ? "(sin ciudad)" : Ciudad;
}

public sealed record ResultadoPwc(IReadOnlyList<FilaPwc> Filas)
{
    public static readonly ResultadoPwc Vacio = new(Array.Empty<FilaPwc>());

    public bool SinDatos => Filas.Count == 0;

    public decimal TotalSubtotal => Filas.Sum(f => f.Subtotal);
    public decimal TotalIva => Filas.Sum(f => f.Iva);
    public decimal TotalFacturado => Filas.Sum(f => f.Total);
    public decimal TotalRetRenta => Filas.Sum(f => f.RetRenta);
    public decimal TotalRetIva => Filas.Sum(f => f.RetIva);
    public decimal TotalDescuentos => Filas.Sum(f => f.Descuentos);
    public decimal TotalMontoCobrar => Filas.Sum(f => f.MontoCobrar);

    /// <summary>
    /// Retenciones por tipo y porcentaje. Sólo cuenta la retención «ganadora» de cada tipo por factura (la
    /// última leída, igual que las columnas L y M de Access), de modo que la suma de todo el resumen
    /// es exactamente <see cref="TotalRetRenta"/> + <see cref="TotalRetIva"/>.
    /// </summary>
    public IReadOnlyList<ResumenRetencionPwc> ResumenRetenciones()
    {
        var todas = Filas
            .SelectMany(f => new[] { f.RetencionRentaGanadora, f.RetencionIvaGanadora })
            .Where(r => r is not null)
            .Select(r => r!);

        return todas
            .GroupBy(r => (r.Tipo, r.Porcentaje))
            .Select(g => new ResumenRetencionPwc(g.Key.Tipo, g.Key.Porcentaje, g.Sum(r => r.Monto), g.Count()))
            .OrderBy(r => r.Tipo)
            .ThenBy(r => r.Porcentaje is null ? 1 : 0)
            .ThenBy(r => r.Porcentaje)
            .ToList();
    }

    /// <summary>Cantidad de facturas y monto a cobrar por ciudad («(sin ciudad)» al final).</summary>
    public IReadOnlyList<ResumenCiudadPwc> ResumenPorCiudad()
        => Filas
            .GroupBy(f => f.Ciudad, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ResumenCiudadPwc(g.Key, g.Count(), g.Sum(f => f.MontoCobrar)))
            .OrderBy(c => c.Ciudad.Length == 0 ? 1 : 0)
            .ThenBy(c => c.Ciudad, StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>Extrae el porcentaje de la descripción de una línea de retención.</summary>
public static partial class PorcentajeRetencion
{
    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*%")]
    private static partial Regex Patron();

    /// <summary>
    /// <c>"309 - RF 3%"</c> → 3 · <c>"1.75% RET IMP RENTA"</c> → 1,75 · <c>"IVA 70%"</c> → 70 ·
    /// <c>"RETENCION IVA"</c> → <c>null</c>. Access buscaba las subcadenas fijas «1%», «2%», «20%» y «70%».
    /// </summary>
    public static decimal? Extraer(string? descripcion)
    {
        if (string.IsNullOrWhiteSpace(descripcion))
        {
            return null;
        }
        var m = Patron().Match(descripcion);
        return m.Success
            && decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var p)
            ? p
            : null;
    }
}

/// <summary>Descripción legible de los filtros (para el subtítulo del Excel).</summary>
public static class DescripcionFiltroPwc
{
    public static string Describir(this FiltroPwc f)
    {
        var partes = new List<string>();
        if (f.EmisionDesde is not null || f.EmisionHasta is not null)
        {
            partes.Add($"Emisión {Fecha(f.EmisionDesde)}–{Fecha(f.EmisionHasta)}");
        }
        if (f.VenceDesde is not null || f.VenceHasta is not null)
        {
            partes.Add($"Cobranza {Fecha(f.VenceDesde)}–{Fecha(f.VenceHasta)}");
        }
        if (!string.IsNullOrWhiteSpace(f.Cliente)) partes.Add($"Cliente contiene «{f.Cliente.Trim()}»");
        if (!string.IsNullOrWhiteSpace(f.Factura)) partes.Add($"Factura contiene «{f.Factura.Trim()}»");
        if (!string.IsNullOrWhiteSpace(f.Anunciante)) partes.Add($"Anunciante contiene «{f.Anunciante.Trim()}»");
        if (f.Ciudades is { Count: > 0 } c)
        {
            partes.Add("Ciudad: " + string.Join(", ", c.Select(x => x.Length == 0 ? "(sin ciudad)" : x)));
        }
        return partes.Count == 0 ? "Sin filtros" : string.Join(" · ", partes);
    }

    private static string Fecha(DateOnly? d) => d is null ? "…" : d.Value.ToString("dd/MM/yyyy");
}
