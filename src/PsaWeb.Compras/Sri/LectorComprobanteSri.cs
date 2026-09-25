using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace PsaWeb.Compras.Sri;

public enum TipoComprobanteSri { NoValido, Factura, Retencion, NotaCredito, NotaDebito }

/// <summary>Comprobante ya desenvuelto (sin el sobre SOAP ni la <c>&lt;autorizacion&gt;</c> del SRI).</summary>
public sealed record ComprobanteSri(TipoComprobanteSri Tipo, string? Version, XElement? Raiz, string? Error);

/// <summary>
/// Desenvuelve un XML del SRI y reconoce el tipo de comprobante. Port de <c>ImportXmlLib.readDocument.XmlReader</c>:
/// acepta la respuesta SOAP de <c>AutorizacionComprobantesOffline</c> (el comprobante viene escapado dentro de
/// <c>&lt;comprobante&gt;</c>), el archivo <c>&lt;autorizacion&gt;</c> que baja el portal (comprobante en CDATA) y el
/// comprobante suelto. Exige <c>id="comprobante"</c> en la raíz, como el `.exe`.
/// </summary>
public static class LectorComprobanteSri
{
    public static ComprobanteSri Leer(string contenido)
    {
        XDocument documento;
        try
        {
            documento = XDocument.Parse(contenido.TrimStart('﻿'));
        }
        catch (XmlException ex)
        {
            return new(TipoComprobanteSri.NoValido, null, null, "El archivo no es un XML válido: " + ex.Message);
        }

        var raiz = documento.Root!;
        if (raiz.Name.LocalName != "factura" && raiz.Name.LocalName != "comprobanteRetencion"
            && raiz.Name.LocalName != "notaCredito" && raiz.Name.LocalName != "notaDebito")
        {
            // Sobre SOAP o <autorizacion>: el comprobante es el texto (escapado o CDATA) de <comprobante>.
            var envuelto = raiz.DescendantsAndSelf()
                .FirstOrDefault(e => e.Name.LocalName == "comprobante" && e.Value.TrimStart().StartsWith('<'));
            if (envuelto is null)
            {
                return new(TipoComprobanteSri.NoValido, null, null, "Formato de archivo XML no válido");
            }
            try
            {
                raiz = XDocument.Parse(envuelto.Value.Trim()).Root!;
            }
            catch (XmlException ex)
            {
                return new(TipoComprobanteSri.NoValido, null, null, "El comprobante del XML no es válido: " + ex.Message);
            }
        }

        var tipo = raiz.Name.LocalName switch
        {
            "factura" => TipoComprobanteSri.Factura,
            "comprobanteRetencion" => TipoComprobanteSri.Retencion,
            "notaCredito" => TipoComprobanteSri.NotaCredito,
            "notaDebito" => TipoComprobanteSri.NotaDebito,
            _ => TipoComprobanteSri.NoValido,
        };
        if (tipo == TipoComprobanteSri.NoValido)
        {
            return new(tipo, null, null, "Formato de archivo XML no válido");
        }
        if ((string?)raiz.Attribute("id") != "comprobante")
        {
            return new(TipoComprobanteSri.NoValido, null, null, "No se encuentra el atributo: (id = \"comprobante\")");
        }
        return new(tipo, (string?)raiz.Attribute("version"), raiz, null);
    }

    /// <summary>Nombre del tipo como lo muestra el `.exe` al rechazar un comprobante que no es factura.</summary>
    public static string Nombre(TipoComprobanteSri tipo) => tipo switch
    {
        TipoComprobanteSri.Factura => "Factura",
        TipoComprobanteSri.Retencion => "Retención",
        TipoComprobanteSri.NotaCredito => "Nota de Crédito",
        TipoComprobanteSri.NotaDebito => "Nota de Débito",
        _ => "No válido",
    };

    // ---- helpers compartidos por los lectores de cada tipo ----

    internal static string Texto(XElement? padre, string nombre) => (string?)padre?.Element(nombre) ?? string.Empty;

    internal static string? TextoONulo(XElement? padre, string nombre)
    {
        var v = (string?)padre?.Element(nombre);
        return string.IsNullOrEmpty(v) ? null : v;
    }

    /// <summary>
    /// Número del XML. El `.exe` usaba <c>Convert.ToDouble</c> con la cultura del equipo (y 0 si falta el nodo);
    /// acá cultura invariante: el XML del SRI siempre usa punto decimal.
    /// </summary>
    internal static decimal Numero(XElement? padre, string nombre)
    {
        var v = (string?)padre?.Element(nombre);
        return string.IsNullOrWhiteSpace(v) ? 0m : decimal.Parse(v.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture);
    }
}
