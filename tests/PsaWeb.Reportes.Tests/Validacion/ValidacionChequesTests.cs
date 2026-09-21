using System.Data.Odbc;
using System.Globalization;
using PsaWeb.Modules.Reportes.Cheques;

namespace PsaWeb.Reportes.Tests.Validacion;

/// <summary>
/// Valida la lista y el detalle de pagos contra el Sage local de Radio FM Efemedio, comparando con las consultas
/// TAL CUAL las corría Access (fecha literal <c>{ d 'yyyy-mm-dd' }</c>, una consulta de detalle por pago con
/// <c>PostOrder = n</c>) y con una re-implementación literal de <c>NumberReference</c> con la semántica de
/// <c>InStr</c>/<c>Mid</c> de VB. Opcional: se salta sin la variable de entorno de <see cref="EntornoSage"/>.
/// </summary>
public class ValidacionChequesTests
{
    private static readonly FiltroCheques Rango = new(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 30));

    /// <summary>
    /// <c>NumberReference</c> de <c>Modules_general</c> (ReportesEgresosDemoR) sin tocar su lógica. Devuelve
    /// <c>null</c> donde Access lanza un error en tiempo de ejecución (<c>Mid</c> con largo negativo).
    /// </summary>
    private static string? NumberReferenceVb(string r)
    {
        static int InStr(string s, string t) => s.IndexOf(t, StringComparison.Ordinal) + 1;      // 1-based, 0 = no está
        static string Mid(string s, int inicio) => inicio > s.Length ? "" : s[(inicio - 1)..];
        static string MidLen(string s, int inicio, int largo) => largo < 0 ? throw new InvalidOperationException("Mid con largo negativo") : Mid(s, inicio).Length <= largo ? Mid(s, inicio) : Mid(s, inicio)[..largo];
        static bool IsNumeric(string s) => s.Trim().Length > 0 && s.Trim().All(char.IsAsciiDigit);

        var prefixPos = InStr(r, "-");
        var strNumber = Mid(r, prefixPos + 1);
        if (!IsNumeric(strNumber))
        {
            var secondPos = InStr(strNumber, "-") + prefixPos;
            var tamPrefix = prefixPos;
            var tamSufix = r.Length - secondPos + 1;
            try
            {
                strNumber = MidLen(r, prefixPos + 1, r.Length - tamPrefix - tamSufix);
            }
            catch (InvalidOperationException)
            {
                return null; // Access: «Invalid procedure call»
            }
        }
        return strNumber;
    }

    [SkippableFact]
    public async Task La_lista_de_pagos_coincide_con_la_consulta_y_el_filtro_de_Access()
    {
        Skip.If(EntornoSage.CadenaEfemedio is null, "Sin PSAWEB_TEST_SAGE_EFEMEDIO (Sage local de Efemedio).");

        var acceso = EntornoSage.Acceso(EntornoSage.CadenaEfemedio!);
        var lista = await new OdbcChequesRepository(acceso).ListarAsync(Rango);

        // Referencia: la consulta de Access tal cual y su filtro por referencia numérica.
        await using var cn = await acceso.AbrirSesionAsync(default);
        await using var cmd = new OdbcCommand(
            "SELECT JrnlHdr.PostOrder, JrnlHdr.TransactionDate, JrnlHdr.TrxName, JrnlHdr.Reference, JrnlHdr.MainAmount " +
            "FROM JrnlHdr WHERE (JrnlHdr.JrnlKey_Journal = 2) AND (JrnlHdr.JournalEx = 5) AND (JrnlHdr.JrnlTypeEx = 0) " +
            "AND (JrnlHdr.TransactionDate BETWEEN { d '2026-08-01' } AND { d '2026-09-30' }) ORDER BY JrnlHdr.TransactionDate DESC;", cn);
        var esperados = new List<(long Po, string Ref, string Numero, string Tipo, decimal Monto)>();
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                if (r.IsDBNull(3)) continue;
                var referencia = Convert.ToString(r.GetValue(3), CultureInfo.InvariantCulture)!.Trim();
                var numero = NumberReferenceVb(referencia);
                if (numero is null || numero.Trim().Length == 0 || !numero.Trim().All(char.IsAsciiDigit)) continue;
                var dash = referencia.IndexOf('-') + 1;
                var tipo = dash > 0 ? referencia[..dash] : "Default";
                var monto = Math.Abs(Math.Round(Convert.ToDecimal(r.GetValue(4), CultureInfo.InvariantCulture), 2, MidpointRounding.ToEven));
                esperados.Add((Convert.ToInt64(r.GetValue(0)), referencia, numero.Trim(), tipo, monto));
            }
        }

        Assert.NotEmpty(esperados);
        Assert.Equal(esperados.Count, lista.Count);
        Assert.Equal(esperados.Select(e => e.Po).OrderBy(x => x), lista.Select(p => p.PostOrder).OrderBy(x => x));

        var porPo = lista.ToDictionary(p => p.PostOrder);
        foreach (var e in esperados)
        {
            var p = porPo[e.Po];
            Assert.Equal(e.Ref, p.Referencia.Principal);
            Assert.Equal(e.Numero, p.Referencia.Numero);
            Assert.Equal(e.Tipo, p.Referencia.Tipo);
            Assert.Equal(e.Monto, p.Monto);
        }

        // Hay de los dos mundos: cheques «puros» (Default) y con prefijo.
        Assert.Contains(lista, p => p.Referencia.Tipo == "Default");
        Assert.Contains(lista, p => p.Referencia.Tipo != "Default");
    }

    [SkippableFact]
    public async Task El_detalle_coincide_con_la_consulta_por_pago_de_Access_y_el_asiento_cuadra()
    {
        Skip.If(EntornoSage.CadenaEfemedio is null, "Sin PSAWEB_TEST_SAGE_EFEMEDIO (Sage local de Efemedio).");

        var acceso = EntornoSage.Acceso(EntornoSage.CadenaEfemedio!);
        var repo = new OdbcChequesRepository(acceso);
        var lista = await repo.ListarAsync(Rango);
        var muestra = lista.Take(25).Select(p => p.PostOrder).ToList();

        var detalles = await repo.DetallesAsync(muestra);
        Assert.Equal(muestra, detalles.Select(d => d.Pago.PostOrder).ToList()); // mismo orden que se pidió

        await using var cn = await acceso.AbrirSesionAsync(default);
        foreach (var d in detalles)
        {
            // Consulta de detalle TAL CUAL Access (una por pago, PostOrder concatenado).
            await using var cmd = new OdbcCommand(
                "SELECT JrnlRow.RowNumber, Chart.AccountID, Chart.AccountDescription, JrnlRow.RowDescription, JrnlRow.Amount, " +
                "Bills.Reference AS BillNum, Bills.Description AS BillVendor " +
                "FROM Chart, { oj JrnlRow LEFT OUTER JOIN JrnlHdr Bills ON JrnlRow.LinkToAnotherTrx = Bills.PostOrder } " +
                $"WHERE JrnlRow.GLAcntNumber = Chart.GLAcntNumber AND (JrnlRow.PostOrder = {d.Pago.PostOrder}) ORDER BY JrnlRow.RowNumber;", cn);
            var esperadas = new List<(int Fila, string Cuenta, string Desc, decimal Importe, string Factura)>();
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    esperadas.Add((
                        Convert.ToInt32(r.GetValue(0)),
                        (r.IsDBNull(1) ? "" : Convert.ToString(r.GetValue(1))!).Trim(),
                        (r.IsDBNull(3) ? "" : Convert.ToString(r.GetValue(3))!).Trim(),
                        Math.Round(Convert.ToDecimal(r.GetValue(4), CultureInfo.InvariantCulture), 2, MidpointRounding.ToEven),
                        (r.IsDBNull(5) ? "" : Convert.ToString(r.GetValue(5))!).Trim()));
                }
            }

            Assert.Equal(esperadas.Count, d.Lineas.Count);
            for (var i = 0; i < esperadas.Count; i++)
            {
                Assert.Equal(esperadas[i].Fila, d.Lineas[i].NumeroFila);
                Assert.Equal(esperadas[i].Cuenta, d.Lineas[i].CuentaId);
                Assert.Equal(esperadas[i].Desc, d.Lineas[i].Descripcion);
                Assert.Equal(esperadas[i].Importe, d.Lineas[i].Importe);
                Assert.Equal(esperadas[i].Factura, d.Lineas[i].Factura);
            }

            // Como en el reporte de Access, el pie muestra el monto del pago en PAGOS y en CHEQUE: el asiento cuadra
            // salvo pagos anulados (monto 0 y sin líneas con importe).
            if (d.Pago.Monto > 0m)
            {
                Assert.Equal(d.Pago.Monto, d.Lineas.Sum(l => l.Pagos));
                Assert.Equal(d.Pago.Monto, d.Lineas.Sum(l => l.Cheque));
            }
        }
    }

    [SkippableFact]
    public async Task Solo_se_devuelven_pagos_reales_aunque_se_pidan_otros_asientos()
    {
        Skip.If(EntornoSage.CadenaEfemedio is null, "Sin PSAWEB_TEST_SAGE_EFEMEDIO (Sage local de Efemedio).");

        var repo = new OdbcChequesRepository(EntornoSage.Acceso(EntornoSage.CadenaEfemedio!));

        // PostOrder 1 y 2 nunca son pagos (diario 2 / tipo 5): no se imprimen aunque se pidan a mano en la URL.
        Assert.Empty(await repo.DetallesAsync(new long[] { 1, 2, 3 }));
        Assert.Empty(await repo.DetallesAsync(new long[] { 999999999 }));
    }
}
