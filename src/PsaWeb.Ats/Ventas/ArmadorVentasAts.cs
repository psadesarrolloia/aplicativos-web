using PsaWeb.Ats.Esquema;

namespace PsaWeb.Ats.Ventas;

/// <summary>
/// Arma y fusiona las filas de <c>detalleVentasType</c> del ATS a partir de lo
/// leído de Sage 50. Parte <b>pura</b> (sin ODBC) — port de la lógica de
/// <c>LoadSales</c> que no consulta la base: el bucle principal de fusión por
/// cliente duplicado y el cálculo de <c>totalVentas</c>.
/// </summary>
public static class ArmadorVentasAts
{
    /// <summary>
    /// Arma una fila del esquema a partir de lo leído para un
    /// (cliente, tipo de comprobante). No fusiona — eso lo hace
    /// <see cref="Fusionar"/> sobre la lista completa.
    /// </summary>
    public static detalleVentasType ArmarDetalle(FilaVentaCruda fila)
    {
        var detalle = new detalleVentasType
        {
            tpIdCliente = fila.Cliente.TipoIdentificacion!,
            idCliente = fila.Cliente.Identificacion,
            // Parte relacionada: Sage 50 no tiene un campo dedicado, así que
            // se marca a mano en la ficha del cliente — convención acordada
            // con el usuario 2026-09-12: escribir "SI" en el campo "Account
            // Number" (Customers.AccountNumber) de los clientes relacionados.
            // Ese mismo campo se reusa más abajo como tipoCliente para
            // clientes del exterior; si un cliente es a la vez exterior y
            // relacionado, el texto "SI" queda también como tipoCliente —
            // se replica tal cual la convención, sin tratar de adivinar.
            parteRelVtas = EsParteRelacionada(fila.Cliente.TipoCliente) ? parteRelType.SI : parteRelType.NO,
            parteRelVtasSpecified = true,
            numeroComprobantes = fila.NumeroComprobantes,
            tipoEmision = tipoEmisionType.F,
            tipoComprobante = fila.TipoComprobante,
            baseNoGraIva = fila.Buckets.BaseNoGraIva,
            // Bug B1 (preservado — ver BucketsVentaAts): en el `.exe`,
            // LoadMontoIVA pisa baseImponible a 0 justo después de que
            // LoadBaseImponible la calculó; el valor real nunca llega al XML.
            baseImponible = 0m,
            baseImpGrav = fila.Buckets.BaseImpGrav,
            montoIva = fila.Buckets.MontoIva,
            montoIce = 0m,
            montoIceSpecified = true,
            valorRetIva = fila.Buckets.ValorRetIva,
            valorRetRenta = fila.Buckets.ValorRetRenta,
        };

        // tipoCliente/denoCli solo se informan para clientes del exterior.
        if (fila.Cliente.TipoIdentificacion == TiposIdentificacionClienteAts.Exterior)
        {
            detalle.tipoCliente = fila.Cliente.TipoCliente;
            detalle.denoCli = fila.Cliente.Nombre;
        }

        // Forma de pago fija en "20" para facturas, ausente en NC — decisión
        // 2026-09-12 (docs/PLAN-APP3-ATS.md §1.4 hallazgo 2): no se condiciona
        // por fecha/monto, se preserva el comportamiento tal cual del `.exe`.
        if (fila.TipoComprobante == "18")
        {
            detalle.formasDePago = new[] { "20" };
        }

        return detalle;
    }

    /// <summary>
    /// true si el "Account Number" del cliente marca "parte relacionada"
    /// (convención 2026-09-12: la palabra exacta "SI", sin distinguir
    /// mayúsculas/espacios).
    /// </summary>
    private static bool EsParteRelacionada(string tipoCliente) =>
        string.Equals(tipoCliente.Trim(), "SI", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Arma cada fila cruda y fusiona las que comparten
    /// <c>(idCliente, tipoComprobante)</c> — ocurre cuando el mismo
    /// contribuyente tiene más de un registro de <c>Customers</c> en Sage.
    /// Port fiel de <c>LoadSales</c>: incluye el <b>Bug B2</b>, el esquema
    /// declara <c>numeroComprobantes</c> como <c>string</c> y el `.exe` lo
    /// fusiona con <c>+=</c> (concatena texto, no suma números).
    /// </summary>
    public static List<detalleVentasType> Fusionar(IEnumerable<FilaVentaCruda> filas)
    {
        var resultado = new List<detalleVentasType>();
        var indice = new Dictionary<(string? IdCliente, string? TipoComprobante), detalleVentasType>();

        foreach (var fila in filas)
        {
            var detalle = ArmarDetalle(fila);
            var clave = (detalle.idCliente, detalle.tipoComprobante);

            if (indice.TryGetValue(clave, out var existente))
            {
                existente.numeroComprobantes += detalle.numeroComprobantes; // Bug B2: concatenación de texto.
                existente.baseImpGrav += detalle.baseImpGrav;
                existente.baseImponible += detalle.baseImponible;
                existente.baseNoGraIva += detalle.baseNoGraIva;
                existente.montoIva += detalle.montoIva;
                existente.valorRetIva += detalle.valorRetIva;
                existente.valorRetRenta += detalle.valorRetRenta;
            }
            else
            {
                indice[clave] = detalle;
                resultado.Add(detalle);
            }
        }

        return resultado;
    }

    /// <summary>
    /// Total de ventas del período: suma de <c>baseImpGrav + baseImponible +
    /// baseNoGraIva</c> por comprobante, restando las notas de crédito. Como
    /// la fusión solo suma campos numéricos, da lo mismo calcularlo antes o
    /// después de <see cref="Fusionar"/> — acá se usa después.
    /// </summary>
    public static decimal TotalVentas(IEnumerable<detalleVentasType> detalles)
    {
        decimal total = 0;
        foreach (var d in detalles)
        {
            var subtotal = d.baseImpGrav + d.baseImponible + d.baseNoGraIva;
            total += d.tipoComprobante == "04" ? -subtotal : subtotal;
        }

        return total;
    }
}
