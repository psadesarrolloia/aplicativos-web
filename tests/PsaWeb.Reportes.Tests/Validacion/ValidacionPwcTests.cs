using ClosedXML.Excel;
using PsaWeb.Modules.Reportes.Pwc;

namespace PsaWeb.Reportes.Tests.Validacion;

/// <summary>
/// Valida el reporte PWC real (repositorio ODBC + armador) contra el Sage local de Radio FM Efemedio y contra
/// el Excel que generó Access (<c>CXC PWC EFEMEDIO.xlsx</c>). Opcional: se salta sin las variables de entorno
/// de <see cref="EntornoSage"/> o sin el archivo.
/// <para>
/// El Excel del usuario es POSTERIOR a la copia de Sage de PREDATOR: tiene 3 facturas nuevas
/// (<c>…15614</c>, <c>…15615</c>, <c>…15618</c>) y 2 retenciones (<c>…15603</c>, <c>…15611</c>) cargadas
/// después. Por eso se exige coincidencia exacta de todo lo que ya existe en ambos lados salvo esas
/// retenciones tardías.
/// </para>
/// </summary>
public class ValidacionPwcTests
{
    private static readonly string[] FacturasPosterioresAlCorte =
        { "001-001-000015614", "001-001-000015615", "001-001-000015618" };

    private static readonly string[] RetencionesPosterioresAlCorte =
        { "001-001-000015603", "001-001-000015611" };

    [SkippableFact]
    public async Task Repositorio_ODBC_reproduce_el_excel_de_Access()
    {
        Skip.If(EntornoSage.CadenaEfemedio is null, "Sin PSAWEB_TEST_SAGE_EFEMEDIO (Sage local de Efemedio).");
        Skip.IfNot(File.Exists(EntornoSage.RutaExcelPwc), "Excel de referencia no está en esta máquina.");

        var repo = new OdbcPwcRepository(EntornoSage.Acceso(EntornoSage.CadenaEfemedio!));
        var resultado = await repo.GenerarAsync(new FiltroPwc());
        var porFactura = resultado.Filas.ToDictionary(f => f.Factura);

        using var wb = new XLWorkbook(EntornoSage.RutaExcelPwc);
        var ws = wb.Worksheet(1);

        var filasExcel = 0;
        var diferencias = new List<string>();
        for (var r = 8; !ws.Cell(r, "B").IsEmpty(); r++)
        {
            filasExcel++;
            var factura = ws.Cell(r, "B").GetString();
            if (FacturasPosterioresAlCorte.Contains(factura))
            {
                Assert.DoesNotContain(factura, porFactura.Keys); // aún no existe en la copia local
                continue;
            }

            Assert.True(porFactura.TryGetValue(factura, out var f), $"Falta {factura} en el resultado del repositorio.");

            void Igual(string campo, decimal esperado, decimal actual)
            {
                if (Math.Abs(esperado - actual) > 0.005m)
                {
                    diferencias.Add($"{factura} {campo}: excel={esperado:0.00} web={actual:0.00}");
                }
            }

            // Estos campos NUNCA pueden diferir.
            Assert.Equal(DateOnly.FromDateTime(ws.Cell(r, "C").GetDateTime()), f!.Emision);
            Assert.Equal(DateOnly.FromDateTime(ws.Cell(r, "D").GetDateTime()), f.Vence);
            Assert.Equal(ws.Cell(r, "E").GetString().Trim(), f.Cliente.Trim());
            Assert.Equal(ws.Cell(r, "F").GetString().Trim(), f.Orden.Trim());
            Assert.Equal(ws.Cell(r, "G").GetString().Trim(), f.Ciudad.Trim());
            Assert.Equal(ws.Cell(r, "H").GetString().Trim(), f.Anunciante.Trim());
            Assert.Equal((decimal)ws.Cell(r, "I").GetDouble(), f.Subtotal, 2);
            Assert.Equal((decimal)ws.Cell(r, "J").GetDouble(), f.Iva, 2);
            Assert.Equal((decimal)ws.Cell(r, "K").GetDouble(), f.Total, 2);

            // Retenciones y lo que dependa de ellas: sólo pueden diferir en las 2 cargadas después del corte.
            Igual("L (ret. IR)", (decimal)ws.Cell(r, "L").GetDouble(), f.RetRenta);
            Igual("M (ret. IVA)", (decimal)ws.Cell(r, "M").GetDouble(), f.RetIva);
            Igual("N (descuentos)", (decimal)ws.Cell(r, "N").GetDouble(), f.Descuentos);
            Igual("O (a cobrar)", (decimal)ws.Cell(r, "O").GetDouble(), f.MontoCobrar);
        }

        Assert.Equal(85, filasExcel);
        Assert.Equal(filasExcel - FacturasPosterioresAlCorte.Length, resultado.Filas.Count);

        var facturasConDiferencia = diferencias.Select(d => d.Split(' ')[0]).Distinct().ToList();
        Assert.True(
            facturasConDiferencia.All(RetencionesPosterioresAlCorte.Contains),
            "Diferencias inesperadas:\n" + string.Join("\n", diferencias));
    }

    [SkippableFact]
    public async Task El_resumen_de_retenciones_cuadra_con_las_columnas_y_con_el_cuadro_de_Access()
    {
        Skip.If(EntornoSage.CadenaEfemedio is null, "Sin PSAWEB_TEST_SAGE_EFEMEDIO (Sage local de Efemedio).");

        var repo = new OdbcPwcRepository(EntornoSage.Acceso(EntornoSage.CadenaEfemedio!));
        var resultado = await repo.GenerarAsync(new FiltroPwc());
        var resumen = resultado.ResumenRetenciones();

        // La suma del resumen dinámico es exactamente L + M.
        Assert.Equal(resultado.TotalRetRenta + resultado.TotalRetIva, resumen.Sum(r => r.Total));

        // Las cifras del cuadro de Access (Excel: RET. IR 1 % = 17,60 y la columna de 70 % de IVA) están en el resumen.
        Assert.Equal(17.60m, resumen.Single(r => r.Tipo == TipoRetencion.Renta && r.Porcentaje == 1m).Total);
        Assert.Equal(0m, resumen.Where(r => r.Tipo == TipoRetencion.Renta && r.Porcentaje == 2m).Sum(r => r.Total));
        Assert.Equal(0m, resumen.Where(r => r.Tipo == TipoRetencion.Iva && r.Porcentaje == 20m).Sum(r => r.Total));

        // 70 % de IVA «por descripción» = 5.659,05 en la copia local (6.184,05 en el Excel, que incluye 525,00 de
        // la retención de …15611 cargada después del corte).
        var iva70Descripcion = resultado.Filas
            .Where(f => f.RetencionIvaGanadora is { Porcentaje: 70m })
            .Sum(f => f.RetIva);
        Assert.Equal(5659.05m, iva70Descripcion);
    }

    [SkippableFact]
    public async Task Los_filtros_acotan_el_resultado_real()
    {
        Skip.If(EntornoSage.CadenaEfemedio is null, "Sin PSAWEB_TEST_SAGE_EFEMEDIO (Sage local de Efemedio).");

        var repo = new OdbcPwcRepository(EntornoSage.Acceso(EntornoSage.CadenaEfemedio!));
        var todo = await repo.GenerarAsync(new FiltroPwc());

        var opciones = await repo.OpcionesAsync();
        Assert.Contains("GYE", opciones.Ciudades);
        Assert.NotEmpty(opciones.Clientes);

        var soloGye = await repo.GenerarAsync(new FiltroPwc(Ciudades: new[] { "GYE" }));
        Assert.Equal(9, soloGye.Filas.Count);
        Assert.All(soloGye.Filas, f => Assert.Equal("GYE", f.Ciudad));

        var sep2026 = await repo.GenerarAsync(new FiltroPwc(
            EmisionDesde: new DateOnly(2026, 9, 1), EmisionHasta: new DateOnly(2026, 9, 30)));
        Assert.All(sep2026.Filas, f => Assert.Equal(new DateOnly(2026, 9, 1).Month, f.Emision.Month));
        Assert.True(sep2026.Filas.Count is > 0 && sep2026.Filas.Count < todo.Filas.Count);
    }
}
