using PsaWeb.Comprobantes.Compras;
using PsaWeb.Conciliacion.Data;

namespace PsaWeb.Conciliacion;

/// <summary>Las 4 salidas que el motor resuelve solo, sin tocar el SRI de nuevo (§13.3 del plan).</summary>
public enum ClasificacionConciliacion
{
    /// <summary>Pendiente de registrar en Sage.</summary>
    SoloEnSri,

    /// <summary>Revisar validez — puede ser clave vacía/mal tecleada, no necesariamente inválida (§2.3).</summary>
    SoloEnSage,

    /// <summary>Mismo comprobante, montos que no coinciden (fuera de la tolerancia).</summary>
    ValoresDistintos,

    /// <summary>Montos iguales, pero fecha o RUC emisor distintos — error de captura sin impacto en saldo.</summary>
    MetadataDistinta,

    /// <summary>
    /// Coincide en todo lo que se puede comparar sin el SRI — candidato a la
    /// salida 5 (anulados), pero recién se confirma verificando el estado
    /// (§13.4: botón manual o <c>VerificacionEstadoSriWorker</c>).
    /// </summary>
    CoincidePendienteDeVerificar,
}

public sealed record FilaConciliacion(
    string? ClaveAcceso,
    ComprobanteSriGuardado? Sri,
    CompraSage? Sage,
    ClasificacionConciliacion Clasificacion,
    IReadOnlyList<string> Diferencias);

/// <summary>
/// Full outer join por clave de acceso entre Set A (SRI) y Set B (Sage) — las
/// 4 salidas que se resuelven sin volver a tocar el SRI. La salida 5
/// (anulados) no es responsabilidad de este motor: sale de re-verificar el
/// estado de las filas <see cref="ClasificacionConciliacion.CoincidePendienteDeVerificar"/>
/// (§13.4). Lógica pura — nada de ODBC/EF acá, 100% testeable con listas en memoria.
/// </summary>
public static class MotorConciliacion
{
    public const decimal ToleranciaMontosPorDefecto = 0.01m;

    public static IReadOnlyList<FilaConciliacion> Conciliar(
        IReadOnlyList<ComprobanteSriGuardado> setA,
        IReadOnlyList<CompraSage> setB,
        decimal toleranciaMontos = ToleranciaMontosPorDefecto)
    {
        // Set B indexado por autorización (§13.2: solo Trim(), nunca "arreglar"
        // la clave tecleada en Sage) — las que vienen vacías nunca pueden
        // matchear, van directo a Solo en Sage.
        var porAutorizacion = new Dictionary<string, CompraSage>(StringComparer.Ordinal);
        var sinAutorizacion = new List<CompraSage>();
        foreach (var compra in setB)
        {
            var clave = compra.Autorizacion.Trim();
            if (clave.Length == 0)
            {
                sinAutorizacion.Add(compra);
            }
            else
            {
                porAutorizacion[clave] = compra; // si hay 2 compras con la misma autorización tecleada, la última gana.
            }
        }

        var resultado = new List<FilaConciliacion>();
        var clavesConsumidas = new HashSet<string>(StringComparer.Ordinal);

        foreach (var comprobante in setA)
        {
            if (!porAutorizacion.TryGetValue(comprobante.ClaveAcceso, out var compra))
            {
                resultado.Add(new FilaConciliacion(
                    comprobante.ClaveAcceso, comprobante, null, ClasificacionConciliacion.SoloEnSri, []));
                continue;
            }

            clavesConsumidas.Add(comprobante.ClaveAcceso);

            var diferenciasMontos = CompararMontos(comprobante, compra, toleranciaMontos);
            if (diferenciasMontos.Count > 0)
            {
                resultado.Add(new FilaConciliacion(
                    comprobante.ClaveAcceso, comprobante, compra, ClasificacionConciliacion.ValoresDistintos, diferenciasMontos));
                continue;
            }

            var diferenciasMetadata = CompararMetadata(comprobante, compra);
            resultado.Add(diferenciasMetadata.Count > 0
                ? new FilaConciliacion(comprobante.ClaveAcceso, comprobante, compra, ClasificacionConciliacion.MetadataDistinta, diferenciasMetadata)
                : new FilaConciliacion(comprobante.ClaveAcceso, comprobante, compra, ClasificacionConciliacion.CoincidePendienteDeVerificar, []));
        }

        foreach (var compra in sinAutorizacion)
        {
            resultado.Add(new FilaConciliacion(null, null, compra, ClasificacionConciliacion.SoloEnSage, []));
        }

        foreach (var (clave, compra) in porAutorizacion)
        {
            if (!clavesConsumidas.Contains(clave))
            {
                resultado.Add(new FilaConciliacion(clave, null, compra, ClasificacionConciliacion.SoloEnSage, []));
            }
        }

        return resultado;
    }

    private static List<string> CompararMontos(ComprobanteSriGuardado sri, CompraSage sage, decimal tolerancia)
    {
        var diferencias = new List<string>();
        AgregarSiDifiere(diferencias, "Subtotal", sri.Subtotal, sage.Subtotal, tolerancia);
        AgregarSiDifiere(diferencias, "IVA", sri.Iva, sage.Iva, tolerancia);
        AgregarSiDifiere(diferencias, "Total", sri.Total, sage.Total, tolerancia);
        return diferencias;
    }

    private static void AgregarSiDifiere(List<string> diferencias, string campo, decimal sri, decimal sage, decimal tolerancia)
    {
        if (Math.Abs(sri - sage) > tolerancia)
        {
            diferencias.Add($"{campo}: SRI {sri:F2} vs Sage {sage:F2}");
        }
    }

    /// <summary>
    /// Compara fecha y RUC emisor. No compara "tipo de comprobante": el SRI lo
    /// da como texto ("Factura", "Nota de Crédito"); reconstruirlo del lado de
    /// Sage exigiría duplicar la clasificación por <c>ShipVia</c> que ya hace
    /// el ATS (<c>ResolverTipoYSustentoAsync</c>) — no se trajo a propósito
    /// (§13.1: Set B se mantiene simple). Se agrega si hace falta más adelante.
    /// </summary>
    private static List<string> CompararMetadata(ComprobanteSriGuardado sri, CompraSage sage)
    {
        var diferencias = new List<string>();

        if (sri.FechaEmision != sage.Fecha)
        {
            diferencias.Add($"Fecha: SRI {sri.FechaEmision:dd/MM/yyyy} vs Sage {sage.Fecha:dd/MM/yyyy}");
        }

        if (!string.Equals(sri.RucEmisor, sage.RucProveedor, StringComparison.Ordinal))
        {
            diferencias.Add($"RUC emisor: SRI {sri.RucEmisor} vs Sage {sage.RucProveedor}");
        }

        return diferencias;
    }
}
