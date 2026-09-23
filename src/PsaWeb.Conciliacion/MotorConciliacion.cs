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

/// <summary>
/// Una compra "Solo en Sage" con una autorización tecleada corta (~10 dígitos,
/// por serie de comprobante) no es una clave de acceso del SRI mal tecleada —
/// es una factura física (sin comprobante electrónico), que por definición
/// nunca va a aparecer en el reporte del SRI. Heurística de longitud: la
/// clave de acceso real siempre tiene 49 dígitos.
/// </summary>
public static class DocumentoFisico
{
    public const int LongitudClaveAcceso = 49;

    public static bool EsProbable(string? autorizacion)
    {
        var longitud = autorizacion?.Trim().Length ?? 0;
        return longitud > 0 && longitud != LongitudClaveAcceso;
    }
}

public sealed record FilaConciliacion(
    string? ClaveAcceso,
    ComprobanteSriGuardado? Sri,
    CompraSage? Sage,
    ClasificacionConciliacion Clasificacion,
    IReadOnlyList<string> Diferencias,
    bool EnPlazo = false)
{
    // EnPlazo: retención "Solo en SRI"/"Solo en Sage" cuya fecha todavía cae dentro del plazo que da el SRI para emitirla
    // (RetencionPlazoDias): puede simplemente no haberse emitido o registrado aún, no es necesariamente un problema.
    /// <summary>Tipo de documento: el del SRI si hay comprobante, si no el de la compra en Sage.</summary>
    public TipoDocumentoRecibido Tipo => Sri?.Tipo ?? Sage?.Tipo ?? TipoDocumentoRecibido.Otro;
}

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
        decimal toleranciaMontos = ToleranciaMontosPorDefecto,
        DateOnly? hoy = null,
        int plazoRetencionDias = 5)
    {
        bool EnPlazo(TipoDocumentoRecibido tipo, DateOnly fecha) =>
            hoy is { } h && tipo == TipoDocumentoRecibido.Retencion && plazoRetencionDias > 0
            && h.DayNumber - fecha.DayNumber <= plazoRetencionDias;

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

        // Las retenciones recibidas se registran en Sage como notas de crédito de ventas y no siempre llevan
        // la clave de acceso: si no cruzan por clave se cruzan por RUC emisor + serie, que identifican a un
        // comprobante ("001-005-000035930" de ese emisor). Las facturas y notas de crédito NO tienen este
        // respaldo a propósito: una clave mal tecleada debe verse como "Solo en Sage", no taparse.
        var retencionesPorSerie = new Dictionary<(string Ruc, string Serie), CompraSage>();
        foreach (var compra in setB.Where(c => c.Tipo == TipoDocumentoRecibido.Retencion))
        {
            retencionesPorSerie[(compra.RucProveedor.Trim(), compra.Referencia.Trim())] = compra;
        }

        var resultado = new List<FilaConciliacion>();
        var clavesConsumidas = new HashSet<string>(StringComparer.Ordinal);
        var consumidasPorSerie = new HashSet<long>(); // PostOrder de las retenciones cruzadas por serie.

        foreach (var comprobante in setA)
        {
            var cruzoPorSerie = false;
            if (!porAutorizacion.TryGetValue(comprobante.ClaveAcceso, out var compra))
            {
                if (comprobante.Tipo == TipoDocumentoRecibido.Retencion
                    && retencionesPorSerie.TryGetValue((comprobante.RucEmisor.Trim(), comprobante.SerieComprobante.Trim()), out compra)
                    && !consumidasPorSerie.Contains(compra.PostOrder))
                {
                    cruzoPorSerie = true;
                    consumidasPorSerie.Add(compra.PostOrder);
                }
                else
                {
                    resultado.Add(new FilaConciliacion(
                        comprobante.ClaveAcceso, comprobante, null, ClasificacionConciliacion.SoloEnSri, [],
                        EnPlazo(comprobante.Tipo, comprobante.FechaEmision)));
                    continue;
                }
            }
            else
            {
                clavesConsumidas.Add(comprobante.ClaveAcceso);
            }

            var diferenciasMontos = comprobante.TieneMontos
                ? CompararMontos(comprobante, compra, toleranciaMontos)
                : [];
            if (diferenciasMontos.Count > 0)
            {
                resultado.Add(new FilaConciliacion(
                    comprobante.ClaveAcceso, comprobante, compra, ClasificacionConciliacion.ValoresDistintos, diferenciasMontos));
                continue;
            }

            var diferenciasMetadata = CompararMetadata(comprobante, compra, cruzoPorSerie, plazoRetencionDias);
            resultado.Add(diferenciasMetadata.Count > 0
                ? new FilaConciliacion(comprobante.ClaveAcceso, comprobante, compra, ClasificacionConciliacion.MetadataDistinta, diferenciasMetadata)
                : new FilaConciliacion(comprobante.ClaveAcceso, comprobante, compra, ClasificacionConciliacion.CoincidePendienteDeVerificar, []));
        }

        foreach (var compra in sinAutorizacion.Where(c => !consumidasPorSerie.Contains(c.PostOrder)))
        {
            resultado.Add(new FilaConciliacion(null, null, compra, ClasificacionConciliacion.SoloEnSage, [],
                EnPlazo(compra.Tipo, compra.Fecha)));
        }

        foreach (var (clave, compra) in porAutorizacion)
        {
            if (!clavesConsumidas.Contains(clave) && !consumidasPorSerie.Contains(compra.PostOrder))
            {
                resultado.Add(new FilaConciliacion(clave, null, compra, ClasificacionConciliacion.SoloEnSage, [],
                    EnPlazo(compra.Tipo, compra.Fecha)));
            }
        }

        return resultado;
    }

    /// <summary>
    /// Las retenciones se buscan en una ventana ampliada (±plazo) para poder encontrar a su pareja cuando las fechas
    /// del SRI y de Sage difieren, pero al usuario se le muestran solo las del período pedido: una fila (o pareja) de
    /// retención se queda si alguna de sus dos fechas cae dentro de [desde, hasta]. Las demás filas ya venían acotadas.
    /// </summary>
    public static IReadOnlyList<FilaConciliacion> RecortarAlPeriodo(
        IEnumerable<FilaConciliacion> filas, DateOnly desde, DateOnly hasta)
    {
        bool Dentro(DateOnly? fecha) => fecha is { } f && f >= desde && f <= hasta;
        return filas
            .Where(f => f.Tipo != TipoDocumentoRecibido.Retencion || Dentro(f.Sri?.FechaEmision) || Dentro(f.Sage?.Fecha))
            .ToList();
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
    /// Compara fecha, RUC emisor y —desde que se concilian notas de crédito y retenciones— el tipo de
    /// documento (el de Sage sale del diario/JournalEx, no de <c>ShipVia</c>), la factura que modifica una
    /// nota de crédito y, si una retención cruzó por serie, que su clave tecleada en Sage no contradiga la del SRI.
    /// </summary>
    private static List<string> CompararMetadata(
        ComprobanteSriGuardado sri, CompraSage sage, bool cruzoPorSerie, int plazoRetencionDias)
    {
        var diferencias = new List<string>();

        // Las retenciones se pueden emitir hasta N días después de la venta: entre la fecha del SRI y la de
        // Sage es normal que haya hasta esa distancia. Solo se marca lo que pasa del plazo.
        var toleranciaDias = sri.Tipo == TipoDocumentoRecibido.Retencion ? Math.Max(plazoRetencionDias, 0) : 0;
        if (Math.Abs(sri.FechaEmision.DayNumber - sage.Fecha.DayNumber) > toleranciaDias)
        {
            diferencias.Add($"Fecha: SRI {sri.FechaEmision:dd/MM/yyyy} vs Sage {sage.Fecha:dd/MM/yyyy}");
        }

        if (!string.Equals(sri.RucEmisor, sage.RucProveedor, StringComparison.Ordinal))
        {
            diferencias.Add($"RUC emisor: SRI {sri.RucEmisor} vs Sage {sage.RucProveedor}");
        }

        if (sri.Tipo != TipoDocumentoRecibido.Otro && sri.Tipo != sage.Tipo)
        {
            diferencias.Add($"Tipo: SRI {TiposDocumentoRecibido.Etiqueta(sri.Tipo)} vs Sage {TiposDocumentoRecibido.Etiqueta(sage.Tipo)}");
        }

        // Solo si ambos lados lo tienen: un vacío en Sage no debe inundar de falsas diferencias.
        if (sri.Tipo == TipoDocumentoRecibido.NotaCredito && sage.Tipo == TipoDocumentoRecibido.NotaCredito
            && !string.IsNullOrWhiteSpace(sri.NumeroDocumentoModificado)
            && !string.IsNullOrWhiteSpace(sage.DocumentoModificado)
            && !string.Equals(sri.NumeroDocumentoModificado.Trim(), sage.DocumentoModificado.Trim(), StringComparison.Ordinal))
        {
            diferencias.Add($"Documento modificado: SRI {sri.NumeroDocumentoModificado.Trim()} vs Sage {sage.DocumentoModificado.Trim()}");
        }

        if (cruzoPorSerie && !string.IsNullOrWhiteSpace(sage.Autorizacion)
            && !string.Equals(sage.Autorizacion.Trim(), sri.ClaveAcceso, StringComparison.Ordinal))
        {
            diferencias.Add($"Clave de acceso: SRI {sri.ClaveAcceso} vs Sage {sage.Autorizacion.Trim()}");
        }

        return diferencias;
    }
}
