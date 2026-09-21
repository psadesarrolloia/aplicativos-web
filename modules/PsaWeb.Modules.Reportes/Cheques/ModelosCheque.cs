using System.Globalization;

namespace PsaWeb.Modules.Reportes.Cheques;

/// <summary>
/// Referencia de un pago de Sage: prefijo + número. Port fiel de <c>NumberReference</c> + la lógica de
/// <c>LoadByDate</c> de <c>ReportesEgresosDemoR</c> (Access):
/// <list type="bullet">
///   <item><c>3094</c> → tipo <c>Default</c>, número 3094 (un cheque «puro»; era lo único que mostraba la versión
///   de <c>ReportesEmpresas</c>).</item>
///   <item><c>PI-1234</c> → tipo <c>PI-</c> (el prefijo INCLUYE el guion), número 1234.</item>
///   <item><c>TRF-015-A</c> → tipo <c>TRF-</c>, número 015 (lo que hay entre el 1.er y el 2.º guion).</item>
/// </list>
/// Sólo se aceptan pagos cuyo número resulte numérico. <b>Q5</b>: Access revienta (<c>Mid</c> con largo negativo)
/// con referencias como <c>PI-ABC</c> o <c>PI-</c>; aquí simplemente se descartan. Además, <c>IsNumeric</c> de VB
/// acepta «1e3», «$5» o «1,5»; aquí el número es de sólo dígitos (es un n.º de cheque).
/// </summary>
public sealed record ReferenciaPago(string Principal, string Tipo, string Numero, long NumeroEntero)
{
    public const string TipoPorDefecto = "Default";

    public static ReferenciaPago? Analizar(string? referencia)
    {
        if (string.IsNullOrWhiteSpace(referencia))
        {
            return null;
        }

        var r = referencia;
        var primerGuion = r.IndexOf('-');               // 0-based; -1 si no hay
        var resto = primerGuion < 0 ? r : r[(primerGuion + 1)..];

        string numero;
        if (EsNumero(resto))
        {
            numero = resto.Trim();
        }
        else
        {
            // Entre el 1.er y el 2.º guion (TRF-015-A → 015). Sin 2.º guion, Access falla: se descarta.
            var segundo = resto.IndexOf('-');
            if (primerGuion < 0 || segundo < 0)
            {
                return null;
            }
            numero = resto[..segundo];
            if (!EsNumero(numero))
            {
                return null;
            }
            numero = numero.Trim();
        }

        if (!long.TryParse(numero, NumberStyles.None, CultureInfo.InvariantCulture, out var entero))
        {
            return null;
        }

        var tipo = primerGuion >= 0 ? r[..(primerGuion + 1)] : TipoPorDefecto;
        return new ReferenciaPago(r, tipo, numero, entero);
    }

    private static bool EsNumero(string s) => s.Trim().Length > 0 && s.Trim().All(char.IsAsciiDigit);
}

/// <summary>Un pago (cabecera) tal como se lista para elegir qué imprimir.</summary>
public sealed record PagoCheque(
    long PostOrder,
    DateOnly Fecha,
    string Beneficiario,
    ReferenciaPago Referencia,
    decimal Monto)
{
    /// <summary>El texto en letras de este monto pierde los centavos por el bug Q1.</summary>
    public bool PierdeCentavosPorBugQ1 => Comun.NumeroALetras.PierdeCentavosPorBugQ1(Monto);
}

/// <summary>Una línea del asiento del pago (comprobante de egreso).</summary>
public sealed record LineaPago(
    int NumeroFila,
    string CuentaId,
    string CuentaDescripcion,
    string Descripcion,
    decimal Importe,
    string Factura,
    string ProveedorFactura)
{
    /// <summary><c>PAGOS()</c> del reporte: <c>RowAmount</c> si <c>Amount &gt; 0</c>, si no 0.</summary>
    public decimal Pagos => Importe > 0m ? Importe : 0m;

    /// <summary><c>CHEQUE()</c> del reporte: <c>Abs(RowAmount)</c> si <c>Amount &lt; 0</c>, si no 0.</summary>
    public decimal Cheque => Importe < 0m ? -Importe : 0m;
}

public sealed record PagoConDetalle(PagoCheque Pago, IReadOnlyList<LineaPago> Lineas);

/// <summary>Filtros de la lista de pagos. Las fechas van en el SQL; lo demás, en memoria.</summary>
public sealed record FiltroCheques(
    DateOnly Desde,
    DateOnly Hasta,
    long? NumeroDesde = null,
    long? NumeroHasta = null,
    string? Tipo = null,
    string? Beneficiario = null)
{
    public bool RangoFechasValido => Desde <= Hasta;
    public bool RangoNumerosValido => NumeroDesde is null || NumeroHasta is null || NumeroDesde <= NumeroHasta;

    public bool Coincide(PagoCheque p)
    {
        if (NumeroDesde is { } d && p.Referencia.NumeroEntero < d) return false;
        if (NumeroHasta is { } h && p.Referencia.NumeroEntero > h) return false;
        if (!string.IsNullOrEmpty(Tipo) && !string.Equals(p.Referencia.Tipo, Tipo, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(Beneficiario)
            && CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                   p.Beneficiario, Beneficiario.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0)
        {
            return false;
        }
        return true;
    }
}

/// <summary>Qué partes de la hoja se imprimen.</summary>
public sealed record OpcionesImpresion(bool Cheque = true, bool Comprobante = true)
{
    public bool Valida => Cheque || Comprobante;
}
