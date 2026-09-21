using PsaWeb.Modules.Reportes.Pwc;

namespace PsaWeb.Modules.Reportes.Comisiones;

/// <summary>
/// Lógica pura del reporte de Comisiones. Port fiel de <c>ReportpComissions.ReportToExcel</c> (Access):
/// <list type="bullet">
///   <item>Se agrupa por cliente con un corte de control cada vez que cambia <c>CustVendId</c> del recibo
///   (las filas ya vienen ordenadas por nombre del recibo). La fila de cabecera muestra el n.º de recibo de la
///   primera fila del cliente (artefacto <b>X1</b> del original).</item>
///   <item>Subtotal e IVA: igual que PWC (último grupo no cero gana, <b>P3</b>).</item>
///   <item>Retención = <c>SUM(Amount)</c> de las notas de crédito «4R» (sin valor absoluto); cruce =
///   <c>Abs(SUM(Amount))</c> de los asientos <c>CJ%</c>.</item>
///   <item><b>C1</b> — <c>ABONO = PAID − CRUCE − RET</c> con <c>PAID = AmountPaid</c> de la <b>factura</b>
///   (no lo aplicado por el recibo). Con <c>AbonoPorRecibo</c> se usa <c>Abs(JrnlRow.Amount)</c> del recibo.</item>
/// </list>
/// </summary>
public static class ArmadorComisiones
{
    public static ResultadoComisiones Armar(
        IReadOnlyList<CobroCrudo> cobros,
        IEnumerable<GrupoImpuestoCrudo> grupos,
        IReadOnlyDictionary<long, decimal> retenciones,
        IReadOnlyDictionary<long, decimal> cruces,
        bool abonoPorRecibo = false)
    {
        var gruposPorFactura = grupos.GroupBy(g => g.PostOrder).ToDictionary(g => g.Key, g => g.ToList());

        var resultado = new List<GrupoComision>();
        long? clienteActual = null;
        List<FilaComision>? filas = null;
        long clienteId = 0;
        string cliente = "", reciboEncabezado = "";

        void CerrarGrupo()
        {
            if (filas is { Count: > 0 })
            {
                resultado.Add(new GrupoComision(clienteId, cliente, reciboEncabezado, filas));
            }
        }

        foreach (var c in cobros)
        {
            if (clienteActual != c.ClienteId)
            {
                CerrarGrupo();
                clienteActual = c.ClienteId;
                clienteId = c.ClienteId;
                cliente = c.Cliente;
                reciboEncabezado = c.Recibo;
                filas = new List<FilaComision>();
            }

            decimal subtotal = 0m, iva = 0m;
            if (gruposPorFactura.TryGetValue(c.PostOrderFactura, out var gs))
            {
                foreach (var g in gs.OrderBy(x => x.RowType).ThenBy(x => x.TaxAuthorityCode, StringComparer.Ordinal))
                {
                    if (g.Subt == 0m) continue;
                    if (g.RowType == 0) subtotal = Math.Abs(g.Subt);
                    else iva = Math.Abs(g.Subt);
                }
            }

            var ret = retenciones.TryGetValue(c.PostOrderFactura, out var r) ? r : 0m;
            var cruce = cruces.TryGetValue(c.PostOrderFactura, out var cr) ? Math.Abs(cr) : 0m;

            decimal abono;
            if (abonoPorRecibo)
            {
                abono = Math.Abs(c.ImporteRecibo);
            }
            else
            {
                abono = c.Pagado is { } pagado ? pagado - cruce - ret : 0m;
            }

            filas!.Add(new FilaComision(
                c.PostOrderFactura, c.Factura, c.Fecha, c.Ciudad, subtotal, iva, c.Total, ret, cruce, abono,
                c.Saldo, c.Recibo, c.FechaRecibo, c.ImporteRecibo));
        }
        CerrarGrupo();

        return new ResultadoComisiones(resultado);
    }
}
