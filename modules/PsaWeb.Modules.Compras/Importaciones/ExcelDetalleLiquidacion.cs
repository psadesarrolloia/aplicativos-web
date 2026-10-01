using System.Globalization;
using ClosedXML.Excel;
using PsaWeb.Compras.Importaciones;

namespace PsaWeb.Modules.Compras.Importaciones;

/// <summary>Hoja leída del Excel: encabezados (primera fila con datos) y filas como celdas.</summary>
public sealed record HojaExcel(IReadOnlyList<string> Columnas, IReadOnlyList<IReadOnlyList<CeldaExcel>> Filas);

/// <summary>Una celda: el número si la celda es numérica, o su texto.</summary>
public sealed record CeldaExcel(double? Numero, string Texto);

/// <summary>
/// «Importar» detalles desde Excel (<c>DetailsFromExcel</c> + <c>ExcelToDataTable</c> del `.exe`): primera hoja, encabezados en la
/// primera fila con datos (sin vacíos; repetidos con sufijo «_n»), el usuario asocia columnas a Item Id / Cantidad / Valor Subtotal.
/// B1: el `.exe` leía el texto con formato de la celda y lo convertía con la cultura del equipo (<c>1234.56</c> en es-EC →
/// 123456). Aquí una celda numérica se toma tal cual, y un texto se convierte con <see cref="Numero"/>.
/// </summary>
public static class ExcelDetalleLiquidacion
{
    public static HojaExcel Leer(Stream archivo)
    {
        using var libro = new XLWorkbook(archivo);
        var hoja = libro.Worksheets.First();
        var rango = hoja.RangeUsed() ?? throw new InvalidOperationException("No se encontró información en el archivo seleccionado.");
        var primera = rango.FirstRow().RowNumber();
        var ultimaCol = rango.LastColumn().ColumnNumber();
        var columnas = new List<string>();
        for (var c = rango.FirstColumn().ColumnNumber(); c <= ultimaCol; c++)
        {
            var nombre = hoja.Cell(primera, c).GetFormattedString().Trim();
            if (nombre.Length == 0)
            {
                throw new InvalidOperationException($"Se buscaron los encabezados en la fila {primera}: hay columnas sin nombre.");
            }
            var repetidas = columnas.Count(x => x == nombre || x.StartsWith(nombre + "_", StringComparison.Ordinal));
            columnas.Add(repetidas > 0 ? $"{nombre}_{repetidas + 1}" : nombre);
        }

        var filas = new List<IReadOnlyList<CeldaExcel>>();
        for (var f = primera + 1; f <= rango.LastRow().RowNumber(); f++)
        {
            var fila = new List<CeldaExcel>();
            for (var c = rango.FirstColumn().ColumnNumber(); c <= ultimaCol; c++)
            {
                var celda = hoja.Cell(f, c);
                fila.Add(celda.DataType == XLDataType.Number
                    ? new CeldaExcel(celda.GetDouble(), celda.GetFormattedString().Trim())
                    : new CeldaExcel(null, celda.GetFormattedString().Trim()));
            }
            if (fila.All(x => x.Numero is null && x.Texto.Length == 0)) continue; // filas vacías: el `.exe` las reportaba como ítem no encontrado
            filas.Add(fila);
        }
        if (filas.Count == 0) throw new InvalidOperationException("No se encontró información en el archivo seleccionado.");
        return new HojaExcel(columnas, filas);
    }

    /// <summary>
    /// Convierte las filas a ítems con la asociación de columnas elegida (<c>ImportFromExcel_importDataEvent</c>): el ítem debe existir
    /// entre los de stock (la descripción sale de Sage); prorrateo en 0. Devuelve los ítems y los mensajes por fila (fila = la del Excel).
    /// </summary>
    public static (List<ItemLiquidacion> Items, List<string> Mensajes) Convertir(HojaExcel hoja, int colItem, int colCantidad, int colValor,
        IReadOnlyDictionary<string, ItemStock> items)
    {
        var resultado = new List<ItemLiquidacion>();
        var mensajes = new List<string>();
        for (var i = 0; i < hoja.Filas.Count; i++)
        {
            var fila = hoja.Filas[i];
            var n = i + 2;
            var id = fila[colItem].Texto.Trim();
            if (!items.TryGetValue(id, out var item))
            {
                mensajes.Add($"Fila {n}: no se encontró el ítem de stock «{id}».");
                continue;
            }
            var cantidad = Numero(fila[colCantidad], out var errorCantidad);
            var valor = Numero(fila[colValor], out var errorValor);
            if (errorCantidad is not null) mensajes.Add($"Fila {n}, {hoja.Columnas[colCantidad]}: {errorCantidad}");
            if (errorValor is not null) mensajes.Add($"Fila {n}, {hoja.Columnas[colValor]}: {errorValor}");
            resultado.Add(new ItemLiquidacion { ItemId = item.Id, Descripcion = item.Descripcion, Cantidad = cantidad, Valor = valor });
        }
        return (resultado, mensajes);
    }

    /// <summary>
    /// Número de una celda. Numérica: su valor. Texto: sin símbolo de moneda ni espacios; con «.» y «,» el último es el decimal; con
    /// solo uno de los dos, es el decimal salvo que aparezca más de una vez (miles). Vacío o no convertible: 0 y el error (como el
    /// `.exe`, que ponía 0 y avisaba).
    /// </summary>
    public static double Numero(CeldaExcel celda, out string? error)
    {
        error = null;
        if (celda.Numero is { } d) return d;
        var t = celda.Texto.Replace("$", string.Empty).Replace(" ", string.Empty).Replace(" ", string.Empty);
        if (t.Length == 0)
        {
            error = "vacío (se tomó 0).";
            return 0;
        }
        var punto = t.LastIndexOf('.');
        var coma = t.LastIndexOf(',');
        string normal;
        if (punto >= 0 && coma >= 0)
        {
            normal = punto > coma ? t.Replace(",", string.Empty) : t.Replace(".", string.Empty).Replace(',', '.');
        }
        else if (coma >= 0)
        {
            normal = t.Count(x => x == ',') > 1 ? t.Replace(",", string.Empty) : t.Replace(',', '.');
        }
        else
        {
            normal = t.Count(x => x == '.') > 1 ? t.Replace(".", string.Empty) : t;
        }
        if (double.TryParse(normal, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)) return v;
        error = $"«{celda.Texto}» no es un número (se tomó 0).";
        return 0;
    }
}
