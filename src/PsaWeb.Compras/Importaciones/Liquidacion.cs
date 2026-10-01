using System.Globalization;
using System.Text.RegularExpressions;
using PsaWeb.SageBridge.Contratos;

namespace PsaWeb.Compras.Importaciones;

// Lógica pura de la Liquidación de Importaciones (port fiel de FrmImportMng + sageImportMgm del `.exe`,
// docs/PLAN-OLA2-LIQUIDACION-IMPORTACIONES.md §1 y §6). Los montos van en double, como el `.exe`, para que el prorrateo
// dé exactamente lo mismo; la OC se arma en decimal igual que él (Convert.ToDecimal de la suma).

/// <summary>Estado de una importación (<c>sageImportMgm.ImportStatus</c>).</summary>
public enum EstadoImportacion
{
    /// <summary>La cuenta no tiene movimientos.</summary>
    SinDatos,

    /// <summary>«En Tránsito»: sin liquidación guardada.</summary>
    EnTransito,

    /// <summary>«En Tránsito *»: liquidación guardada, sin OC en Sage.</summary>
    Guardada,

    /// <summary>«En Proceso»: la OC existe en Sage.</summary>
    EnProceso,

    /// <summary>«Liquidada»: el saldo de la cuenta está en ±0,05 (B4).</summary>
    Liquidada,
}

public static class EstadosImportacion
{
    public static string Texto(EstadoImportacion e) => e switch
    {
        EstadoImportacion.SinDatos => "Sin movimientos",
        EstadoImportacion.EnTransito => "En Tránsito",
        EstadoImportacion.Guardada => "En Tránsito *",
        EstadoImportacion.EnProceso => "En Proceso",
        EstadoImportacion.Liquidada => "Liquidada",
        _ => e.ToString(),
    };

    /// <summary>
    /// Regla del constructor de <c>sageImportMgm</c>: sin filas → sin datos; saldo en ±0,05 → liquidada; si no, según haya
    /// liquidación guardada y su OC exista en Sage.
    /// </summary>
    public static EstadoImportacion Calcular(int movimientos, double saldo, bool guardada, bool ocEnSage)
    {
        if (movimientos == 0) return EstadoImportacion.SinDatos;
        if (saldo > -0.05 && saldo < 0.05) return EstadoImportacion.Liquidada;
        if (!guardada) return EstadoImportacion.EnTransito;
        return ocEnSage ? EstadoImportacion.EnProceso : EstadoImportacion.Guardada;
    }

    /// <summary>Solo lectura cuando ya hay OC o está liquidada (<c>LoadStatus</c>).</summary>
    public static bool Editable(EstadoImportacion e) => e is EstadoImportacion.EnTransito or EstadoImportacion.Guardada;
}

/// <summary>Cuenta de importación de Sage con el resumen de sus movimientos (sin las OC).</summary>
public sealed record CuentaImportacion(
    string Cuenta,
    string Descripcion,
    bool Inactiva,
    int Movimientos,
    double Saldo,
    DateTime? Desde,
    DateTime? Hasta);

/// <summary>
/// Una fila de la cuenta de la importación (<c>sageImportDetail</c>): gasto de nacionalización o, con <see cref="EsGasto"/> = false,
/// la factura del exterior.
/// </summary>
public sealed class GastoImportacion
{
    public DateTime Fecha { get; set; }

    /// <summary><c>JrnlHdr.Description</c> (nombre del proveedor en el asiento).</summary>
    public string Proveedor { get; set; } = string.Empty;

    public string Referencia { get; set; } = string.Empty;

    /// <summary><c>JrnlRow.RowDescription</c>.</summary>
    public string Descripcion { get; set; } = string.Empty;

    /// <summary>Monto de la fila redondeado a 2 decimales.</summary>
    public double Valor { get; set; }

    /// <summary><c>isCost</c>: true = gasto; false = factura del exterior.</summary>
    public bool EsGasto { get; set; } = true;

    /// <summary>C2: la fila está en Sage pero no en la liquidación guardada.</summary>
    public bool Nueva { get; set; }

    public GastoImportacion Copia() => (GastoImportacion)MemberwiseClone();
}

/// <summary>Un ítem de la liquidación (<c>ApportionDetail</c>).</summary>
public sealed class ItemLiquidacion
{
    public string ItemId { get; set; } = string.Empty;

    public string Descripcion { get; set; } = string.Empty;

    public double Cantidad { get; set; }

    /// <summary>Valor del ítem en la factura del exterior («Valor Subtotal»).</summary>
    public double Valor { get; set; }

    /// <summary>«Prorrateo %» (0–100, 6 decimales).</summary>
    public double Porcentaje { get; set; }

    /// <summary>«Prorrateado»: parte de los gastos que le toca.</summary>
    public double Prorrateo { get; set; }

    public double ValorUnitario => Cantidad == 0 ? 0 : Valor / Cantidad;

    /// <summary><c>TotalCost</c> = round(prorrateo + valor, 2).</summary>
    public double CostoTotal => Math.Round(Prorrateo + Valor, 2);

    public double CostoUnitario => Cantidad == 0 ? 0 : CostoTotal / Cantidad;

    public ItemLiquidacion Copia() => (ItemLiquidacion)MemberwiseClone();
}

public static class Liquidaciones
{
    /// <summary>Total de gastos (<c>isCost</c>) y de la factura del exterior (<c>updateTotals</c>).</summary>
    public static (double Gastos, double Factura) Totales(IEnumerable<GastoImportacion> gastos)
    {
        double g = 0, f = 0;
        foreach (var x in gastos)
        {
            if (x.EsGasto) g += x.Valor;
            else f += x.Valor;
        }
        return (g, f);
    }

    /// <summary>
    /// «Prorratear» (<c>btnApportion_Click</c>): exige Σ valor de los ítems = factura (a 2 decimales). Recorre los ítems ordenados por
    /// valor (orden estable): % = round(valor / factura, 8), prorrateo = round(gastos × %, 2); el último recibe el residuo y
    /// % = round(1 − Σ%, 4) (B2). Devuelve el error del `.exe` o null.
    /// </summary>
    public static string? Prorratear(IList<ItemLiquidacion> items, double totalFactura, double totalGastos)
    {
        var sumaItems = items.Sum(x => x.Valor);
        if (Math.Round(totalFactura, 2) != Math.Round(sumaItems, 2))
        {
            return $"El valor de la factura del exterior ({totalFactura.ToString("N2", CultureInfo.GetCultureInfo("es-EC"))}) no coincide con " +
                   $"el valor ingresado en los ítems ({sumaItems.ToString("N2", CultureInfo.GetCultureInfo("es-EC"))}).";
        }
        foreach (var x in items)
        {
            x.Porcentaje = 0;
            x.Prorrateo = 0;
        }

        double sumaProrrateo = 0, sumaPorcentaje = 0;
        var i = 1;
        foreach (var x in items.OrderBy(x => x.Valor))
        {
            var porcentaje = Math.Round(x.Valor / totalFactura, 8);
            double valor;
            if (i == items.Count)
            {
                valor = Math.Round(totalGastos - sumaProrrateo, 2);
                porcentaje = Math.Round(1 - sumaPorcentaje, 4);
            }
            else
            {
                valor = Math.Round(totalGastos * porcentaje, 2);
            }
            x.Porcentaje = Math.Round(porcentaje * 100, 6);
            x.Prorrateo = valor;
            sumaProrrateo += valor;
            sumaPorcentaje += porcentaje;
            i++;
        }
        return null;
    }

    /// <summary>¿El costeo está completo? (<c>btnLoad_Click</c>/<c>btnReport_Click</c>): Σ costo total &gt; 0 y = Σ de todas las filas, a 2 decimales.</summary>
    public static bool CosteoCompleto(IEnumerable<ItemLiquidacion> items, IEnumerable<GastoImportacion> gastos)
    {
        var costo = Math.Round(items.Sum(x => x.CostoTotal), 2);
        return costo > 0 && costo == Math.Round(gastos.Sum(x => x.Valor), 2);
    }

    /// <summary>
    /// C2: las filas son las de Sage; la marca de factura (y solo eso) se toma de la liquidación guardada por fecha + referencia +
    /// descripción + monto. El `.exe` usaba la lista guardada solo si la suma coincidía exactamente, y si no perdía las marcas.
    /// </summary>
    public static (List<GastoImportacion> Gastos, List<GastoImportacion> Desaparecidos) Conciliar(
        IReadOnlyList<GastoImportacion> deSage, IReadOnlyList<GastoImportacion>? guardados)
    {
        var resultado = deSage.Select(x => x.Copia()).ToList();
        if (guardados is null || guardados.Count == 0) return (resultado, []);
        var pendientes = guardados.ToList();
        foreach (var g in resultado)
        {
            var par = pendientes.FirstOrDefault(s => Clave(s) == Clave(g));
            if (par is null)
            {
                g.Nueva = true;
                continue;
            }
            g.EsGasto = par.EsGasto;
            pendientes.Remove(par);
        }
        return (resultado, pendientes);
    }

    private static string Clave(GastoImportacion g) =>
        $"{g.Fecha:yyyyMMdd}|{g.Referencia.Trim()}|{g.Descripcion.Trim()}|{Math.Round(g.Valor, 2).ToString("0.00", CultureInfo.InvariantCulture)}";

    /// <summary>Referencia de la OC: prefijo + «-» + número, o el número solo si no hay prefijo (<c>SaveChangesOnDB</c>).</summary>
    public static string Referencia(string? prefijo, string? numero) =>
        string.IsNullOrWhiteSpace(prefijo) ? (numero ?? string.Empty).Trim() : $"{prefijo.Trim()}-{(numero ?? string.Empty).Trim()}";

    /// <summary>Separa una referencia guardada en prefijo y número por el primer guion (<c>updateObjects</c>).</summary>
    public static (string Prefijo, string Numero) Separar(string? referencia)
    {
        if (string.IsNullOrEmpty(referencia)) return (string.Empty, string.Empty);
        var i = referencia.IndexOf('-');
        return i < 0 ? (string.Empty, referencia) : (referencia[..i], referencia[(i + 1)..]);
    }

    /// <summary>
    /// Número propuesto a partir de la cuenta: <c>IMPORTACION 41-2026</c> → <c>041-2026</c> (convención de las OC reales). Nulo si
    /// la descripción no trae «nn-aaaa».
    /// </summary>
    public static string? NumeroPropuesto(string descripcionCuenta)
    {
        var m = Regex.Match(descripcionCuenta, @"(\d+)\s*-\s*(\d{4})");
        return m.Success ? $"{int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture):000}-{m.Groups[2].Value}" : null;
    }

    public const string PrefijoPorDefecto = "LIQ IMPORT";
}

/// <summary>Lo que el usuario confirma al pulsar «Crear OC y compra».</summary>
public sealed record EntradaOcLiquidacion(
    string CuentaImportacion,
    string ProveedorId,
    DateTime Fecha,
    string Referencia,
    int? PostOrder,
    IReadOnlyList<ItemLiquidacion> Items,
    IReadOnlyList<GastoImportacion> Gastos);

public static class ArmadorOcLiquidacion
{
    /// <summary>
    /// Validaciones de «Crear Purchase Order» (<c>btnLoad_Click</c>) y payload del Bridge (<c>loadingPOforImportsCost</c>): una línea
    /// por ítem en el orden de la grilla, monto = Convert.ToDecimal(valor + prorrateo).
    /// </summary>
    public static (PayloadGuardarOcLiquidacion? Payload, List<string> Errores) Armar(EntradaOcLiquidacion e, ISet<string> proveedores)
    {
        var errores = new List<string>();
        if (string.IsNullOrWhiteSpace(e.ProveedorId)) errores.Add("Debe especificar un proveedor para poder cargar la presente liquidación.");
        else if (!proveedores.Contains(e.ProveedorId.Trim())) errores.Add("Debe indicar un proveedor previamente creado en SAGE 50.");
        if (e.Items.Count == 0 || !Liquidaciones.CosteoCompleto(e.Items, e.Gastos))
        {
            errores.Add("El proceso de Costeo está incompleto, el total incluido los costos debe coincidir con el total de importación.");
        }
        if (string.IsNullOrWhiteSpace(e.Referencia)) errores.Add("Debe indicar un número de referencia para subir a SAGE.");
        else if (e.Referencia.Trim().Length > 20) errores.Add("La referencia de la OC no puede pasar de 20 caracteres (límite de Sage).");
        foreach (var x in e.Items.Where(x => string.IsNullOrWhiteSpace(x.ItemId)))
        {
            errores.Add($"Falta el ítem en la línea «{x.Descripcion}».");
        }
        if (errores.Count > 0) return (null, errores);

        return (new PayloadGuardarOcLiquidacion
        {
            CuentaImportacion = e.CuentaImportacion,
            CuentaPorPagar = PsaWeb.Compras.Armado.ArmadorOc.CuentaPorPagar,
            Fecha = e.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Lineas = e.Items.Select(x => new LineaOcLiquidacion
            {
                Cantidad = Convert.ToDecimal(x.Cantidad),
                Descripcion = x.Descripcion,
                Item = x.ItemId.Trim(),
                Monto = Convert.ToDecimal(x.Valor + x.Prorrateo),
            }).ToList(),
            PostOrder = e.PostOrder,
            ProveedorId = e.ProveedorId.Trim(),
            Referencia = e.Referencia.Trim(),
        }, errores);
    }
}
