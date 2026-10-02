namespace PsaWeb.Ventas.Prefacturas;

/// <summary>Resultado de validar una solicitud: los errores impiden emitir; las advertencias solo se muestran (y quedan en la prefactura).</summary>
public sealed record ResultadoValidacion(IReadOnlyList<string> Errores, IReadOnlyList<string> Advertencias)
{
    public bool EsValida => Errores.Count == 0;
}

public static class ValidadorPrefactura
{
    public const int MaximoDeLineas = 200;
    public const decimal PrecioMaximo = 10_000_000m;

    public static ResultadoValidacion Validar(SolicitudPrefactura s, ConfiguracionVentas config)
    {
        var errores = new List<string>();
        var advertencias = new List<string>();

        if (string.IsNullOrWhiteSpace(s.ClienteId)) errores.Add("Elegí el cliente.");
        if (string.IsNullOrWhiteSpace(s.Vendedor)) errores.Add("Elegí el vendedor (el rep de Sage con el que se facturará).");
        if (s.Lineas.Count == 0) errores.Add("Agregá al menos un ítem.");
        if (s.Lineas.Count > MaximoDeLineas) errores.Add($"Máximo {MaximoDeLineas} líneas por prefactura.");

        for (var i = 0; i < s.Lineas.Count; i++)
        {
            var l = s.Lineas[i];
            var n = i + 1;
            if (string.IsNullOrWhiteSpace(l.ItemId)) errores.Add($"Línea {n}: falta el ítem.");
            if (l.Cantidad <= 0) errores.Add($"Línea {n} ({l.ItemId}): la cantidad debe ser mayor que cero.");
            if (l.PrecioUnitario < 0 || l.PrecioUnitario > PrecioMaximo) errores.Add($"Línea {n} ({l.ItemId}): precio fuera de rango.");
            if (l.PrecioUnitario == 0) advertencias.Add($"Línea {n} ({l.ItemId}): el precio es 0.");
            if (l.ExistenciaAlEmitir is { } e && l.Cantidad > e) advertencias.Add($"Línea {n} ({l.ItemId}): pide {Texto(l.Cantidad)} y hay {Texto(Math.Max(e, 0))} en existencia.");
        }

        var duplicados = s.Lineas.GroupBy(l => l.ItemId.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1 && g.Key.Length > 0).Select(g => g.Key);
        foreach (var d in duplicados) advertencias.Add($"El ítem {d} está repetido en varias líneas.");

        if (s.AplicaIva && config.PorcentajeIva <= 0) errores.Add("La empresa no tiene configurado el porcentaje de IVA.");

        if (errores.Count == 0)
        {
            var lineas = CalculadoraPrefactura.Calcular(s.Lineas);
            var (_, _, total) = CalculadoraPrefactura.Totales(lineas, s.AplicaIva, config.PorcentajeIva);
            if (s.LimiteCredito > 0 && s.SaldoCliente + total > s.LimiteCredito)
            {
                advertencias.Add($"El cliente superaría su cupo de crédito: saldo {Texto(s.SaldoCliente)} + esta prefactura {Texto(total)} > límite {Texto(s.LimiteCredito)}.");
            }
        }

        return new ResultadoValidacion(errores, advertencias);
    }

    private static string Texto(decimal v) => v.ToString("#,##0.##", System.Globalization.CultureInfo.GetCultureInfo("es-EC"));
}
