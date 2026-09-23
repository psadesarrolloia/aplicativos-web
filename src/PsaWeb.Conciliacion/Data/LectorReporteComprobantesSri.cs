using System.Globalization;
using System.Text.RegularExpressions;

namespace PsaWeb.Conciliacion.Data;

/// <summary>El reporte no tiene el formato esperado — el portal cambió de estructura, o el archivo no es el correcto.</summary>
public sealed class FormatoReporteInvalidoException(string message) : Exception(message);

public sealed record ComprobanteSriReportado(
    string RucEmisor,
    string RazonSocialEmisor,
    string TipoComprobante,
    string SerieComprobante,
    string ClaveAcceso,
    DateTime FechaAutorizacion,
    DateOnly FechaEmision,
    string IdentificacionReceptor,
    decimal Subtotal,
    decimal Iva,
    decimal Total,
    string? NumeroDocumentoModificado);

public sealed record FilaConError(int NumeroFila, string Motivo);

public sealed record ResultadoParseoReporte(
    IReadOnlyList<ComprobanteSriReportado> Filas,
    IReadOnlyList<FilaConError> Errores);

/// <summary>
/// Parsea el reporte tabulado que descarga "Comprobantes electrónicos
/// recibidos" del portal SRI en Línea (formato real confirmado 2026-09-17:
/// 1 línea por comprobante con encabezado, 12 columnas). No hace I/O — recibe
/// el texto ya decodificado (ISO-8859-1 → Unicode se resuelve antes, del lado
/// de la extensión) y devuelve DTOs en memoria.
/// </summary>
public static class LectorReporteComprobantesSri
{
    private static readonly string[] EncabezadoEsperado =
    [
        "RUC_EMISOR", "RAZON_SOCIAL_EMISOR", "TIPO_COMPROBANTE", "SERIE_COMPROBANTE",
        "CLAVE_ACCESO", "FECHA_AUTORIZACION", "FECHA_EMISION", "IDENTIFICACION_RECEPTOR",
        "VALOR_SIN_IMPUESTOS", "IVA", "IMPORTE_TOTAL", "NUMERO_DOCUMENTO_MODIFICADO",
    ];

    private static readonly Regex ClaveAccesoRegex = new(@"^\d{49}$", RegexOptions.Compiled);

    /// <summary>
    /// Parsea el reporte. <paramref name="rucEsperado"/> es el RUC que declaró
    /// el request de subida — si alguna fila trae un
    /// <c>IDENTIFICACION_RECEPTOR</c> distinto, se rechaza el archivo entero
    /// (protección contra subir el reporte de la empresa equivocada).
    /// </summary>
    /// <exception cref="FormatoReporteInvalidoException">
    /// Encabezado no coincide, archivo vacío del todo, o el receptor de alguna
    /// fila no es <paramref name="rucEsperado"/>.
    /// </exception>
    public static ResultadoParseoReporte Parsear(string contenido, string rucEsperado)
    {
        var lineas = contenido
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0)
            .ToList();

        if (lineas.Count == 0)
        {
            throw new FormatoReporteInvalidoException("El archivo está vacío.");
        }

        var encabezado = lineas[0].Split('\t');
        if (!encabezado.SequenceEqual(EncabezadoEsperado))
        {
            throw new FormatoReporteInvalidoException(
                "El portal cambió el formato del reporte — avisar a soporte.");
        }

        var filas = new List<ComprobanteSriReportado>();
        var errores = new List<FilaConError>();
        var clavesVistas = new HashSet<string>();

        for (var i = 1; i < lineas.Count; i++)
        {
            var numeroFila = i + 1; // 1-based, incluyendo el encabezado, para que el mensaje coincida con lo que vería alguien abriendo el archivo.
            var campos = lineas[i].Split('\t');
            if (campos.Length < EncabezadoEsperado.Length)
            {
                errores.Add(new FilaConError(numeroFila, "Faltan columnas."));
                continue;
            }

            var claveAcceso = campos[4].Trim();
            if (!ClaveAccesoRegex.IsMatch(claveAcceso))
            {
                errores.Add(new FilaConError(numeroFila, $"Clave de acceso inválida: «{claveAcceso}»."));
                continue;
            }

            var identificacionReceptor = campos[7].Trim();
            if (identificacionReceptor != rucEsperado)
            {
                throw new FormatoReporteInvalidoException(
                    $"El reporte no corresponde a la empresa indicada (fila {numeroFila}: receptor «{identificacionReceptor}», esperado «{rucEsperado}»).");
            }

            if (!DateTime.TryParseExact(campos[5].Trim(), "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaAutorizacion))
            {
                errores.Add(new FilaConError(numeroFila, $"Fecha de autorización inválida: «{campos[5]}»."));
                continue;
            }

            if (!DateOnly.TryParseExact(campos[6].Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaEmision))
            {
                errores.Add(new FilaConError(numeroFila, $"Fecha de emisión inválida: «{campos[6]}»."));
                continue;
            }

            if (!TryParseMonto(campos[8], out var subtotal))
            {
                errores.Add(new FilaConError(numeroFila, $"Valor sin impuestos inválido: «{campos[8]}»."));
                continue;
            }

            if (!TryParseMonto(campos[9], out var iva))
            {
                errores.Add(new FilaConError(numeroFila, $"IVA inválido: «{campos[9]}»."));
                continue;
            }

            // El SRI deja IMPORTE_TOTAL vacío en las notas de crédito (sí trae valor sin impuestos e IVA) y
            // los tres montos vacíos en las retenciones. Vacío no es error: el total de la nota de crédito se
            // deriva, y en las retenciones queda en 0 (la conciliación no compara montos de retenciones).
            decimal total;
            if (campos[10].Trim().Length == 0)
            {
                total = subtotal + iva;
            }
            else if (!TryParseMonto(campos[10], out total))
            {
                errores.Add(new FilaConError(numeroFila, $"Importe total inválido: «{campos[10]}»."));
                continue;
            }

            if (!clavesVistas.Add(claveAcceso))
            {
                continue; // Duplicado dentro del mismo archivo (pasa de verdad, ej. CONTECON): se queda con la primera, no es un error.
            }

            var numeroDocumentoModificado = campos[11].Trim();
            filas.Add(new ComprobanteSriReportado(
                RucEmisor: campos[0].Trim(),
                RazonSocialEmisor: campos[1].Trim(),
                TipoComprobante: campos[2].Trim(),
                SerieComprobante: campos[3].Trim(),
                ClaveAcceso: claveAcceso,
                FechaAutorizacion: fechaAutorizacion,
                FechaEmision: fechaEmision,
                IdentificacionReceptor: identificacionReceptor,
                Subtotal: subtotal,
                Iva: iva,
                Total: total,
                NumeroDocumentoModificado: numeroDocumentoModificado.Length == 0 ? null : numeroDocumentoModificado));
        }

        return new ResultadoParseoReporte(filas, errores);
    }

    /// <summary>Un monto vacío cuenta como 0 (retenciones); un texto que no es número sigue siendo error.</summary>
    private static bool TryParseMonto(string texto, out decimal valor)
    {
        var limpio = texto.Trim();
        if (limpio.Length == 0)
        {
            valor = 0m;
            return true;
        }

        return decimal.TryParse(limpio, NumberStyles.Number, CultureInfo.InvariantCulture, out valor);
    }
}
