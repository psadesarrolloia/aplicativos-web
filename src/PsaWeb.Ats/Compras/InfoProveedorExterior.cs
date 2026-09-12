using System.Text.Json;
using PsaWeb.Ats.Esquema;

namespace PsaWeb.Ats.Compras;

public enum TipoProveedorExterior { Ninguno, Persona, Sociedad }

public enum TipoPagoExterior { Ninguno, Local, Exterior }

public enum RespuestaSiNo { Ninguna, Si, No }

/// <summary>
/// Configuración de pago al proveedor del exterior, guardada por Sage 50 como
/// un array JSON de 7 elementos en <c>Vendors.CustomField1</c>. Port de
/// <c>ExternalVendorInfo</c> (<c>Sage50MetaData</c>).
/// </summary>
/// <param name="PagoSujetoRetencionNormaLegal">
/// Se parsea (7º elemento del array) pero <b>el `.exe` nunca lo usa</b> — ver
/// el Bug B3 en <see cref="ArmarPagoExterior"/>.
/// </param>
public sealed record InfoProveedorExterior(
    TipoProveedorExterior TipoProveedor,
    TipoPagoExterior TipoPago,
    string RegimenTipo,
    string PaisPagoGeneral,
    string PaisPago,
    RespuestaSiNo ConvenioDobleTributacion,
    RespuestaSiNo PagoSujetoRetencionNormaLegal)
{
    public static readonly InfoProveedorExterior Ninguna = new(
        TipoProveedorExterior.Ninguno, TipoPagoExterior.Ninguno, string.Empty, string.Empty, string.Empty,
        RespuestaSiNo.Ninguna, RespuestaSiNo.Ninguna);

    /// <summary>
    /// Parsea el JSON de <c>Vendors.CustomField1</c>. Si no es un array de 7
    /// elementos, o no es JSON válido, devuelve <see cref="Ninguna"/> — igual
    /// que el `.exe`, que atrapa cualquier excepción del parseo.
    /// </summary>
    public static InfoProveedorExterior DesdeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Ninguna;
        }

        try
        {
            using var documento = JsonDocument.Parse(json);
            var arreglo = documento.RootElement;
            if (arreglo.ValueKind != JsonValueKind.Array || arreglo.GetArrayLength() != 7)
            {
                return Ninguna;
            }

            return new InfoProveedorExterior(
                TipoProveedor: ConvertirTipoProveedor(arreglo[0].GetString()),
                TipoPago: ConvertirTipoPago(arreglo[1].GetString()),
                RegimenTipo: arreglo[2].GetString() ?? string.Empty,
                PaisPagoGeneral: arreglo[3].GetString() ?? string.Empty,
                PaisPago: arreglo[4].GetString() ?? string.Empty,
                ConvenioDobleTributacion: ConvertirSiNo(arreglo[5].GetString()),
                PagoSujetoRetencionNormaLegal: ConvertirSiNo(arreglo[6].GetString()));
        }
        catch (JsonException)
        {
            return Ninguna;
        }
    }

    public string TipoProveedorTexto() => TipoProveedor switch
    {
        TipoProveedorExterior.Persona => "01",
        TipoProveedorExterior.Sociedad => "02",
        _ => string.Empty,
    };

    private static TipoProveedorExterior ConvertirTipoProveedor(string? texto) => texto switch
    {
        "01" => TipoProveedorExterior.Persona,
        "02" => TipoProveedorExterior.Sociedad,
        _ => TipoProveedorExterior.Ninguno,
    };

    private static TipoPagoExterior ConvertirTipoPago(string? texto) => texto switch
    {
        "01" => TipoPagoExterior.Local,
        "02" => TipoPagoExterior.Exterior,
        _ => TipoPagoExterior.Ninguno,
    };

    private static RespuestaSiNo ConvertirSiNo(string? texto) => texto switch
    {
        "SI" => RespuestaSiNo.Si,
        "NO" => RespuestaSiNo.No,
        _ => RespuestaSiNo.Ninguna,
    };

    /// <summary>
    /// Arma el <c>pagoExteriorType</c> del esquema. Port fiel de la parte de
    /// <c>LoadVendor</c> que usa <see cref="InfoProveedorExterior"/> (no arma
    /// <c>tipoRegiSpecified</c> — eso lo decide quien arma la compra, igual
    /// que en el `.exe`: <c>LoadPurchases</c> lo pone en base a
    /// <c>pagoLocExt</c>, no <c>LoadVendor</c>).
    /// </summary>
    public static pagoExteriorType ArmarPagoExterior(InfoProveedorExterior info)
    {
        var pago = new pagoExteriorType();

        if (info.TipoPago is TipoPagoExterior.Ninguno or TipoPagoExterior.Local)
        {
            // Mismo bloque para "sin info" y "pago local": el `.exe` repite el
            // mismo literal en las dos ramas.
            pago.pagoLocExt = pagoLocExtType.Item01;
            pago.paisEfecPago = "NA";
            pago.aplicConvDobTrib = aplicConvDobTribType.NA;
            pago.pagExtSujRetNorLeg = aplicConvDobTribType.NA;
            return pago;
        }

        // TipoPago == Exterior.
        pago.pagoLocExt = pagoLocExtType.Item02;
        var siConvenio = info.ConvenioDobleTributacion == RespuestaSiNo.Si
            ? aplicConvDobTribType.SI
            : aplicConvDobTribType.NO;

        switch (info.RegimenTipo)
        {
            case "01":
                pago.paisEfecPagoGen = info.PaisPagoGeneral;
                pago.paisEfecPago = info.PaisPago;
                pago.aplicConvDobTrib = siConvenio;
                pago.tipoRegi = tipoRegiType.Item01;
                break;
            case "02":
                pago.paisEfecPagoParFis = info.PaisPagoGeneral;
                pago.paisEfecPago = info.PaisPago;
                pago.aplicConvDobTrib = siConvenio;
                pago.tipoRegi = tipoRegiType.Item02;
                break;
            case "03":
                pago.denopagoRegFis = string.Empty;
                pago.paisEfecPago = info.PaisPago;
                pago.aplicConvDobTrib = siConvenio;
                pago.tipoRegi = tipoRegiType.Item03;
                break;
            // default: el `.exe` no arma nada más — paisEfecPago queda null.
        }

        // Bug B3 (LoadVendor): el `.exe` compara pagoExterior.pagExtSujRetNorLeg
        // contra sí mismo ANTES de asignarlo — siempre lee el default del enum
        // (SI, primer valor declarado en aplicConvDobTribType) y por lo tanto
        // el resultado es SIEMPRE "SI", sin importar lo que diga el JSON (el 7º
        // elemento, arriba en PagoSujetoRetencionNormaLegal, queda sin usar).
        pago.pagExtSujRetNorLeg = aplicConvDobTribType.SI;

        return pago;
    }
}
