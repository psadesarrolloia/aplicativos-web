using System.Globalization;

namespace PsaWeb.Modules.Reportes.Comisiones;

/// <summary>
/// Una fila de la consulta principal: una factura de venta cobrada (en todo o en parte) por un recibo de
/// cobro. Si una factura se pagó con 2 recibos, aparece 2 veces (una por recibo).
/// </summary>
public sealed record CobroCrudo(
    long PostOrderFactura,
    long ClienteId,
    string Cliente,
    string Factura,
    DateOnly Fecha,
    decimal? Total,
    decimal? Saldo,
    decimal? Pagado,
    string Recibo,
    DateOnly FechaRecibo,
    decimal ImporteRecibo,
    string Ciudad);

/// <summary>Una fila de factura del reporte de comisiones.</summary>
public sealed record FilaComision(
    long PostOrderFactura,
    string Factura,
    DateOnly Fecha,
    string Ciudad,
    decimal Subtotal,
    decimal Iva,
    decimal? Total,
    decimal Retencion,
    decimal Cruce,
    decimal Abono,
    decimal? Saldo,
    string Recibo,
    DateOnly FechaRecibo,
    decimal ImporteRecibo);

/// <summary>Un cliente con sus facturas cobradas. <see cref="ReciboEncabezado"/> es el n.º de recibo de su 1.ª fila.</summary>
public sealed record GrupoComision(
    long ClienteId,
    string Cliente,
    string ReciboEncabezado,
    IReadOnlyList<FilaComision> Filas)
{
    public decimal TotalAbono => Filas.Sum(f => f.Abono);
}

public sealed record ResultadoComisiones(IReadOnlyList<GrupoComision> Grupos)
{
    public static readonly ResultadoComisiones Vacio = new(Array.Empty<GrupoComision>());

    public bool SinDatos => Grupos.Count == 0;

    public IEnumerable<FilaComision> Filas => Grupos.SelectMany(g => g.Filas);
    public int Facturas => Filas.Count();
    public decimal TotalSubtotal => Filas.Sum(f => f.Subtotal);
    public decimal TotalIva => Filas.Sum(f => f.Iva);
    public decimal TotalFacturado => Filas.Sum(f => f.Total ?? 0m);
    public decimal TotalRetencion => Filas.Sum(f => f.Retencion);
    public decimal TotalCruce => Filas.Sum(f => f.Cruce);
    public decimal TotalAbono => Filas.Sum(f => f.Abono);
    public decimal TotalSaldo => Filas.Sum(f => f.Saldo ?? 0m);
}

/// <summary>
/// Filtros del reporte de comisiones.
/// <para>
/// <b>C2</b> — el reporte de Access filtra por rango de número de recibo <b>comparando texto</b>
/// (<c>Receipt.Reference &lt;= 'hasta'</c> y <c>&gt;= Format(desde,"000")</c>): <c>'513'</c> queda dentro de
/// <c>5122–5146</c>. Con <see cref="RangoNumerico"/> se compara como número.
/// <b>C1</b> — con <see cref="AbonoPorRecibo"/> el abono es el importe que ese recibo aplicó a la factura, en vez del
/// total pagado de la factura menos cruces y retenciones.
/// </para>
/// </summary>
public sealed record FiltroComisiones(
    string? ReciboDesde = null,
    string? ReciboHasta = null,
    DateOnly? FechaReciboDesde = null,
    DateOnly? FechaReciboHasta = null,
    string? Cliente = null,
    string? Ciudad = null,
    bool RangoNumerico = false,
    bool AbonoPorRecibo = false)
{
    public bool TieneRangoRecibos => !string.IsNullOrWhiteSpace(ReciboDesde) && !string.IsNullOrWhiteSpace(ReciboHasta);
    public bool TieneRangoFechas => FechaReciboDesde is not null || FechaReciboHasta is not null;

    /// <summary>Al menos un acotador: sin él se leería todo el histórico de recibos.</summary>
    public bool TieneAcotador => TieneRangoRecibos || TieneRangoFechas;

    public bool RangoRecibosValido =>
        (string.IsNullOrWhiteSpace(ReciboDesde) && string.IsNullOrWhiteSpace(ReciboHasta))
        || (TieneRangoRecibos && SoloDigitos(ReciboDesde!) && SoloDigitos(ReciboHasta!));

    public bool RangoFechasValido => FechaReciboDesde is null || FechaReciboHasta is null || FechaReciboDesde <= FechaReciboHasta;

    /// <summary><c>Format(numFrom, "000")</c> de Access: al menos 3 dígitos con ceros a la izquierda.</summary>
    public static string FormatearDesde(string desde)
        => long.TryParse(desde.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n.ToString("000", CultureInfo.InvariantCulture)
            : desde.Trim();

    /// <summary>¿El n.º de recibo cae en el rango? (texto como Access, o número con <see cref="RangoNumerico"/>).</summary>
    public bool ReciboEnRango(string recibo)
    {
        if (!TieneRangoRecibos)
        {
            return true;
        }

        if (RangoNumerico)
        {
            return long.TryParse(recibo.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                   && long.TryParse(ReciboDesde!.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var d)
                   && long.TryParse(ReciboHasta!.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var h)
                   && n >= d && n <= h;
        }

        return string.CompareOrdinal(recibo, ReciboHasta!.Trim()) <= 0
               && string.CompareOrdinal(recibo, FormatearDesde(ReciboDesde!)) >= 0;
    }

    public bool Coincide(CobroCrudo c)
    {
        if (!ReciboEnRango(c.Recibo)) return false;
        if (FechaReciboDesde is { } d && c.FechaRecibo < d) return false;
        if (FechaReciboHasta is { } h && c.FechaRecibo > h) return false;
        if (!Contiene(c.Cliente, Cliente)) return false;
        if (!Contiene(c.Ciudad, Ciudad)) return false;
        return true;
    }

    private static bool Contiene(string valor, string? buscado)
        => string.IsNullOrWhiteSpace(buscado)
           || CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                  valor, buscado.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    private static bool SoloDigitos(string s) => s.Trim().Length > 0 && s.Trim().All(char.IsAsciiDigit);
}

/// <summary>Descripción legible de los filtros (para el subtítulo del Excel).</summary>
public static class DescripcionFiltroComisiones
{
    public static string Describir(this FiltroComisiones f)
    {
        var partes = new List<string>();
        if (f.TieneRangoRecibos)
        {
            partes.Add($"Recibos {f.ReciboDesde!.Trim()}–{f.ReciboHasta!.Trim()}" + (f.RangoNumerico ? " (numérico)" : ""));
        }
        if (f.TieneRangoFechas)
        {
            partes.Add($"Fecha del recibo {Fecha(f.FechaReciboDesde)}–{Fecha(f.FechaReciboHasta)}");
        }
        if (!string.IsNullOrWhiteSpace(f.Cliente)) partes.Add($"Cliente contiene «{f.Cliente.Trim()}»");
        if (!string.IsNullOrWhiteSpace(f.Ciudad)) partes.Add($"Ciudad contiene «{f.Ciudad.Trim()}»");
        if (f.AbonoPorRecibo) partes.Add("Abono = importe aplicado por el recibo");
        return partes.Count == 0 ? "Sin filtros" : string.Join(" · ", partes);
    }

    private static string Fecha(DateOnly? d) => d is null ? "…" : d.Value.ToString("dd/MM/yyyy");
}
