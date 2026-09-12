using System.Globalization;

namespace PsaWeb.Ats.Compras;

/// <summary>
/// Clasifica una línea de compra (<c>LineItem</c>+<c>JrnlRow</c>) en los
/// buckets de <see cref="BucketsComprasAts"/>. Parte <b>pura</b> de
/// <c>LoadImponibles</c> (<c>ATSfromPeach</c>), separada del ODBC para poder
/// testearla directo.
/// </summary>
public static class ClasificadorLineasComprasAts
{
    /// <summary>
    /// Acumula una línea sobre <paramref name="acumulado"/> y devuelve el
    /// resultado (sin aplicar todavía abs/round — eso lo hace
    /// <see cref="LectorDetalleComprasAts"/> al final, una sola vez, igual que
    /// el `.exe`).
    /// </summary>
    /// <param name="laborCost">
    /// Se compara como texto contra literales con punto decimal ("0.1", "0.3",
    /// etc.) — <b>hay que formatearlo con <see cref="CultureInfo.InvariantCulture"/></b>:
    /// si se usa la cultura del servidor (en Ecuador, coma decimal) estas
    /// comparaciones nunca calzan y las retenciones de IVA por compra quedan
    /// siempre en 0. Verificado que el XML real de CPTDC sí trae estos valores
    /// (0.1=10%, 0.2=20%, 0.3=30%, 0.5=50%, 0.7=70%, 1.0=100%), así que el
    /// `.exe` compara en cultura invariante (o equivalente) — no la del SO.
    /// </param>
    public static BucketsComprasAts AcumularLinea(
        BucketsComprasAts acumulado, string category, string customField1, string customField3, string customField4,
        decimal amount, decimal laborCost)
    {
        category = category.ToUpperInvariant();
        var cf1 = customField1.ToUpperInvariant();
        var cf3 = customField3.ToUpperInvariant();
        var cf4 = customField4.ToUpperInvariant();
        var laborCostTexto = laborCost.ToString(CultureInfo.InvariantCulture);

        if (category.Contains("IMPUESTO"))
        {
            if (cf1.Contains("IVA"))
            {
                acumulado = acumulado with { MontoIva = acumulado.MontoIva + amount };
            }
        }
        else if (category.Contains("R-IVA"))
        {
            if (cf1.Contains('9') && laborCostTexto.Contains("0.1"))
            {
                acumulado = acumulado with { ValRetBien10 = acumulado.ValRetBien10 + amount };
            }

            if (cf1.Contains("10") && laborCostTexto.Contains("0.2"))
            {
                acumulado = acumulado with { ValRetServ20 = acumulado.ValRetServ20 + amount };
            }

            if (cf1.Contains('1') && laborCostTexto.Contains("0.3"))
            {
                acumulado = acumulado with { ValorRetBienes = acumulado.ValorRetBienes + amount };
            }

            if (cf1.Contains("11") && laborCostTexto.Contains("0.5"))
            {
                acumulado = acumulado with { ValRetServ50 = acumulado.ValRetServ50 + amount };
            }

            if (cf1.Contains('2') && laborCostTexto.Contains("0.7"))
            {
                acumulado = acumulado with { ValorRetServicios = acumulado.ValorRetServicios + amount };
            }

            if (cf1.Contains('3') && laborCostTexto.Contains("1.0"))
            {
                acumulado = acumulado with { ValRetServ100 = acumulado.ValRetServ100 + amount };
            }
        }
        else if (!category.Contains("R-IRF"))
        {
            if (cf3.Contains("NO"))
            {
                if (cf4.Contains("IMPEXE"))
                {
                    acumulado = acumulado with { BaseImpExe = acumulado.BaseImpExe + amount };
                }
                else if (cf4.Contains("NOGRAIVA"))
                {
                    acumulado = acumulado with { BaseNoGraIva = acumulado.BaseNoGraIva + amount };
                }
                else if (cf4.Contains("IMPONIBLE"))
                {
                    acumulado = acumulado with { BaseImponible = acumulado.BaseImponible + amount };
                }
            }
            else
            {
                acumulado = acumulado with { BaseImpGrav = acumulado.BaseImpGrav + amount };
            }
        }

        return acumulado;
    }

    /// <summary>Redondea (abs + 2 decimales) todos los buckets — una sola vez, al final.</summary>
    public static BucketsComprasAts Redondear(BucketsComprasAts b) => new(
        Corregir(b.BaseNoGraIva), Corregir(b.BaseImponible), Corregir(b.BaseImpGrav), Corregir(b.BaseImpExe),
        Corregir(b.MontoIva), Corregir(b.ValRetBien10), Corregir(b.ValRetServ20), Corregir(b.ValorRetBienes),
        Corregir(b.ValRetServ50), Corregir(b.ValorRetServicios), Corregir(b.ValRetServ100));

    private static decimal Corregir(decimal valor) => Math.Round(Math.Abs(valor), 2);
}
