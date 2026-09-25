using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PsaWeb.SageBridge.Contratos;

/// <summary>
/// Numeración de la OC y de la retención (port de <c>sagePurchaseOrders.OC_newIPoReference</c> y <c>twhNumNext</c>, §6.3
/// del plan). Pura: el Bridge le pasa lo que lee de Sage por ODBC en el momento de guardar.
/// </summary>
public static class NumeracionCompras
{
    /// <summary>
    /// Siguiente nº de OC del prefijo: máximo <b>numérico</b> de <c>PREFIJO-n</c> + 1, con al menos 4 dígitos.
    /// Corrección C4: el `.exe` tomaba el máximo de texto de <c>PREFIJO-____</c> (4 dígitos exactos) y se trababa en 9999.
    /// </summary>
    public static string SiguienteOc(string prefijo, IEnumerable<string> referenciasExistentes)
    {
        var inicio = prefijo + "-";
        var maximo = referenciasExistentes
            .Select(r => (r ?? string.Empty).Trim())
            .Where(r => r.StartsWith(inicio, StringComparison.OrdinalIgnoreCase) && r.Length > inicio.Length
                        && r.Substring(inicio.Length).All(char.IsDigit))
            .Select(r => long.Parse(r.Substring(inicio.Length), CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max();
        return inicio + (maximo + 1).ToString("0000", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Siguiente nº de retención de la serie (<c>001-001</c>): máximo de <c>ShipToAddress2</c> con formato
    /// <c>serie-#########</c> + 1; si queda por debajo de <paramref name="secuencialInicial"/> (<c>startNumerationTwh</c>), ese.
    /// </summary>
    public static string SiguienteRetencion(string serie, IEnumerable<string> numerosExistentes, int secuencialInicial = 0)
    {
        var maximo = numerosExistentes
            .Select(n => (n ?? string.Empty).Trim())
            .Where(n => EsNumeroRetencion(n) && n.StartsWith(serie + "-", StringComparison.Ordinal))
            .Select(n => int.Parse(n.Substring(8), CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max();
        var siguiente = Math.Max(maximo + 1, secuencialInicial);
        return serie + "-" + siguiente.ToString("000000000", CultureInfo.InvariantCulture);
    }

    /// <summary><c>000-000-000000000</c> (propiedad <c>Bill.TwhNum</c> del `.exe`).</summary>
    public static bool EsNumeroRetencion(string? numero) =>
        numero != null && numero.Length == 17 && numero[3] == '-' && numero[7] == '-'
        && numero.Substring(0, 3).All(char.IsDigit) && numero.Substring(4, 3).All(char.IsDigit) && numero.Substring(8).All(char.IsDigit);

    /// <summary>Nº de OC escrito a mano: <c>XX-</c> + 4 o más dígitos.</summary>
    public static bool EsNumeroOc(string? numero) =>
        numero != null && numero.Length >= 7 && numero[2] == '-' && numero.Substring(0, 2).All(char.IsLetterOrDigit)
        && numero.Substring(3).All(char.IsDigit);
}
