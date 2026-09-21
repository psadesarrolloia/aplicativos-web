namespace PsaWeb.Modules.Reportes.Pwc;

/// <summary>
/// Lógica pura del reporte PWC: junta la cabecera de cada factura con sus grupos de impuesto y sus
/// retenciones. Port fiel de <c>ReportPWC.ReportToExcel</c> (Access).
/// <list type="bullet">
///   <item><b>P3</b> — subtotal e IVA: se recorren los grupos (<c>RowType</c>, <c>TaxAuthorityCode</c>) en
///   orden y <b>el último con monto distinto de cero gana</b>; no se suman. <c>RowType = 0</c> es el
///   subtotal, cualquier otro es IVA. Con los datos de Efemedio los grupos sobrantes son de monto 0.</item>
///   <item><b>P6</b> — retenciones: cada línea <c>IRF</c>/<c>IVA</c> sobrescribe la columna (gana la última);
///   si una factura tuviera dos retenciones de renta, sólo cuenta la segunda.</item>
///   <item>Monto a cobrar = <c>MainAmount − AmountPaid</c> (o <c>MainAmount</c> si <c>AmountPaid</c> es nulo,
///   o 0 si <c>MainAmount</c> es nulo).</item>
/// </list>
/// </summary>
public static class ArmadorPwc
{
    public static IReadOnlyList<FilaPwc> Armar(
        IReadOnlyList<CabeceraFacturaPwc> cabeceras,
        IEnumerable<GrupoImpuestoCrudo> grupos,
        IEnumerable<RetencionCruda> retenciones)
    {
        var gruposPorFactura = grupos.GroupBy(g => g.PostOrder).ToDictionary(g => g.Key, g => g.ToList());
        var retencionesPorFactura = retenciones.GroupBy(r => r.FacturaPostOrder).ToDictionary(g => g.Key, g => g.ToList());

        var filas = new List<FilaPwc>(cabeceras.Count);
        foreach (var c in cabeceras)
        {
            decimal subtotal = 0m, iva = 0m;
            if (gruposPorFactura.TryGetValue(c.PostOrder, out var gs))
            {
                foreach (var g in gs.OrderBy(x => x.RowType).ThenBy(x => x.TaxAuthorityCode, StringComparer.Ordinal))
                {
                    if (g.Subt == 0m)
                    {
                        continue;
                    }
                    if (g.RowType == 0)
                    {
                        subtotal = Math.Abs(g.Subt);
                    }
                    else
                    {
                        iva = Math.Abs(g.Subt);
                    }
                }
            }

            RetencionPwc? renta = null, retIva = null;
            if (retencionesPorFactura.TryGetValue(c.PostOrder, out var rs))
            {
                foreach (var r in rs)
                {
                    var tipo = r.JobId.Trim();
                    if (string.Equals(tipo, "IRF", StringComparison.OrdinalIgnoreCase))
                    {
                        renta = Convertir(TipoRetencion.Renta, r);
                    }
                    else if (string.Equals(tipo, "IVA", StringComparison.OrdinalIgnoreCase))
                    {
                        retIva = Convertir(TipoRetencion.Iva, r);
                    }
                }
            }

            var total = c.Total ?? 0m;
            var cobrar = c.Total is null ? 0m : c.Total.Value - (c.Pagado ?? 0m);

            filas.Add(new FilaPwc(
                c.PostOrder, c.Factura, c.Emision, c.Vence, c.Cliente, c.Orden, c.Ciudad, c.Anunciante, c.Direccion,
                subtotal, iva, total, renta?.Monto ?? 0m, retIva?.Monto ?? 0m, cobrar, renta, retIva));
        }
        return filas;
    }

    private static RetencionPwc Convertir(TipoRetencion tipo, RetencionCruda r)
        => new(tipo, Math.Abs(r.Monto), PorcentajeRetencion.Extraer(r.Descripcion), r.Descripcion);

    /// <summary>Ciudades y clientes presentes, para los selectores de filtro.</summary>
    public static OpcionesPwc Opciones(IEnumerable<CabeceraFacturaPwc> cabeceras)
    {
        var lista = cabeceras.ToList();
        var ciudades = lista.Select(c => c.Ciudad).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c.Length == 0 ? 1 : 0).ThenBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        var clientes = lista.Select(c => c.Cliente).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        return new OpcionesPwc(ciudades, clientes);
    }
}
