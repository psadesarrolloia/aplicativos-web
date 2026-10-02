using System.Net;
using System.Text;

namespace PsaWeb.Ventas.Prefacturas;

/// <summary>
/// Correo a Contabilidad con <b>todos los datos para digitar la factura a mano en Sage 50</b> (primera etapa, sin escritura): los campos de la
/// pantalla de facturación con su valor, cada línea con la lista de precios del cliente y marca si el vendedor cambió el precio, los totales que
/// Sage debe mostrar y las notas internas. El PDF para el cliente va adjunto.
/// </summary>
public static class PrefacturaCorreo
{
    public static string Asunto(Prefactura p) =>
        $"[Prefactura {p.NumeroTexto}] {p.ClienteNombre} — total {FormatoVentas.Dinero(p.Total)} — {p.Vendedor}";

    public static string CuerpoHtml(Prefactura p, string? urlPrefactura = null)
    {
        var h = new StringBuilder();
        h.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:13px;color:#1b1b1b;max-width:820px\">");
        h.Append($"<h2 style=\"color:#163154;margin:0 0 4px\">Prefactura {E(p.NumeroTexto)}</h2>");
        h.Append($"<p style=\"margin:0 0 12px;color:#555\">{E(p.EmpresaNombre)} · emitida el {FormatoVentas.Fecha(p.FechaEmision)} por {E(p.CreadaPor)} · " +
                 $"<b>válida hasta el {FormatoVentas.Fecha(p.ValidaHasta)}</b></p>");
        h.Append("<p>Para <b>digitar la factura en Sage 50</b> con estos datos (el PDF para el cliente va adjunto). " +
                 "Esta prefactura <b>no</b> escribe nada en Sage ni consume el número de factura del SRI.</p>");

        h.Append("<h3 style=\"color:#B40046;margin:16px 0 6px\">Datos de la factura</h3>");
        h.Append("<table style=\"border-collapse:collapse;width:100%\">");
        Fila(h, "Customer ID (cliente)", $"{p.ClienteId} — {p.ClienteNombre}");
        Fila(h, "Sales Rep (vendedor)", p.Vendedor);
        Fila(h, "Customer PO (etiqueta impresa)", p.Etiqueta);
        Fila(h, "Terms (términos)", p.Terminos.Length > 0 ? p.Terminos : "(según el cliente en Sage)");
        Fila(h, "Sales Tax (código de IVA)", p.CodigoImpuesto.Length > 0 ? $"{p.CodigoImpuesto} ({p.PorcentajeIva.ToString("0.##", FormatoVentas.Ec)} %)" : "(sin IVA)");
        Fila(h, "Lista de precios del cliente", p.ListaDePrecios.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (p.OrdenCliente.Length > 0) Fila(h, "Orden del cliente", p.OrdenCliente);
        if (p.DireccionEnvio.Length > 0) Fila(h, "Ship To (entrega)", p.DireccionEnvio);
        if (p.NotaCliente.Length > 0) Fila(h, "Nota para el cliente", p.NotaCliente);
        h.Append("</table>");

        h.Append("<h3 style=\"color:#B40046;margin:16px 0 6px\">Ítems</h3>");
        h.Append("<table style=\"border-collapse:collapse;width:100%\"><thead><tr style=\"background:#163154;color:#fff\">");
        foreach (var c in new[] { "#", "Item ID", "Descripción", "Cant.", "Precio unit.", "Lista " + p.ListaDePrecios, "Monto", "Existencia" })
        {
            h.Append($"<th style=\"padding:5px 6px;text-align:{(c is "#" or "Item ID" or "Descripción" ? "left" : "right")}\">{E(c)}</th>");
        }
        h.Append("</tr></thead><tbody>");
        foreach (var l in p.Lineas)
        {
            var color = l.PrecioManual ? "background:#fff4d6" : string.Empty;
            h.Append($"<tr style=\"{color}\">");
            Td(h, l.Orden.ToString(System.Globalization.CultureInfo.InvariantCulture), false);
            Td(h, l.ItemId, false);
            Td(h, l.Descripcion, false);
            Td(h, FormatoVentas.Cantidad(l.Cantidad) + (l.UnidadMedida.Length > 0 ? " " + l.UnidadMedida : string.Empty), true);
            Td(h, FormatoVentas.Dinero(l.PrecioUnitario) + (l.PrecioManual ? " ✎" : string.Empty), true);
            Td(h, l.PrecioLista is { } pl ? FormatoVentas.Dinero(pl) : "sin precio", true);
            Td(h, FormatoVentas.Dinero(l.Monto), true);
            Td(h, l.ExistenciaAlEmitir is { } e ? FormatoVentas.Cantidad(e) : "—", true);
            h.Append("</tr>");
        }
        h.Append("</tbody></table>");
        h.Append("<p style=\"color:#7a5b00;margin:6px 0 0\">✎ = el vendedor ingresó el precio manualmente (distinto de la lista del cliente en Sage). La existencia es la de Sage al emitir.</p>");

        h.Append("<table style=\"border-collapse:collapse;margin:12px 0 0 auto;min-width:260px\">");
        FilaTotal(h, "Subtotal", p.Subtotal, false);
        if (p.Iva > 0) FilaTotal(h, $"IVA {p.PorcentajeIva.ToString("0.##", FormatoVentas.Ec)} %", p.Iva, false);
        FilaTotal(h, "TOTAL a ver en Sage", p.Total, true);
        h.Append("</table>");

        if (p.NotaInterna.Length > 0)
        {
            h.Append("<h3 style=\"color:#B40046;margin:16px 0 6px\">Notas internas</h3>");
            h.Append($"<p style=\"margin:0;white-space:pre-wrap\">{E(p.NotaInterna)}</p>");
        }

        if (!string.IsNullOrWhiteSpace(urlPrefactura))
        {
            h.Append($"<p style=\"margin-top:16px\">Al facturar, anotá el número de la factura de Sage en la prefactura: <a href=\"{E(urlPrefactura!)}\">{E(p.NumeroTexto)}</a>.</p>");
        }
        h.Append("</div>");
        return h.ToString();
    }

    private static string E(string s) => WebUtility.HtmlEncode(s);

    private static void Fila(StringBuilder h, string campo, string valor) =>
        h.Append($"<tr><td style=\"padding:4px 8px;border:1px solid #ddd;background:#f4f6fa;width:240px\"><b>{E(campo)}</b></td>" +
                 $"<td style=\"padding:4px 8px;border:1px solid #ddd\">{E(valor)}</td></tr>");

    private static void Td(StringBuilder h, string texto, bool derecha) =>
        h.Append($"<td style=\"padding:4px 6px;border-bottom:1px solid #ddd;text-align:{(derecha ? "right" : "left")}\">{E(texto)}</td>");

    private static void FilaTotal(StringBuilder h, string etiqueta, decimal valor, bool fuerte) =>
        h.Append($"<tr><td style=\"padding:3px 10px;{(fuerte ? "font-weight:700;border-top:2px solid #163154" : string.Empty)}\">{E(etiqueta)}</td>" +
                 $"<td style=\"padding:3px 10px;text-align:right;{(fuerte ? "font-weight:700;border-top:2px solid #163154" : string.Empty)}\">{E(FormatoVentas.Dinero(valor))}</td></tr>");
}
