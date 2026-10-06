using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PsaWeb.Compras.Armado;
using PsaWeb.Compras.Bridge;
using PsaWeb.Compras.Catalogo;
using PsaWeb.Compras.Sage;
using PsaWeb.SageBridge.Cola;
using PsaWeb.SageBridge.Cola.Data;
using PsaWeb.SageBridge.Contratos;
using Xunit.Abstractions;

namespace PsaWeb.Compras.Tests.Validacion;

/// <summary>
/// Compra mixta de <see cref="TiposTrabajo.ConvertirOcs"/> (PLAN-OLA2-COMPRAS-SAGE, punto abierto 2026-10-01): <b>ESCRIBE en la copia</b>.
/// Copia una OC real con ítems de inventario como la web («Copiar»: <see cref="RecargaOc"/> + <see cref="ArmadorOc"/>, factura
/// <c>999-996-…</c>), la guarda con <see cref="TiposTrabajo.GuardarOc"/>, la convierte con <see cref="TiposTrabajo.ConvertirOcs"/> y la
/// compara con la compra que hizo el worker COM para la original: compra vinculada a la OC, filas de inventario marcadas
/// (<c>IncludeInInvLedger</c> = 1), mismas filas contables, mismas capas de <c>InventoryCosts</c> y OC cerrada.
/// <para>Solo con <c>PSAWEB_TEST_COMPRA_MIXTA=1</c>, <c>PSAWEB_TEST_SAGE_COMPRAS</c> y el Bridge en consola. OC origen:
/// <c>PSAWEB_TEST_COMPRA_MIXTA_OC</c> (por defecto <c>OC-8021</c>).</para>
/// </summary>
public class CompraMixtaCopiaTests(ITestOutputHelper salida)
{
    private sealed class FabricaCola : IDbContextFactory<SageBridgeDbContext>
    {
        public SageBridgeDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<SageBridgeDbContext>().UseSqlServer(EntornoCompras.PlataformaLocal).Options);
    }

    [SkippableFact]
    public async Task Compra_mixta_vinculada_y_con_inventario()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("PSAWEB_TEST_COMPRA_MIXTA") == "1", "Escribe en la copia: solo con PSAWEB_TEST_COMPRA_MIXTA=1.");
        Skip.If(EntornoCompras.CadenaSage is null, "Sin PSAWEB_TEST_SAGE_COMPRAS.");
        Skip.If(Environment.Is64BitProcess, "El driver ODBC de Pervasive es de 32 bits.");
        var cola = new ColaSage(new FabricaCola());
        Skip.IfNot((await cola.LatidosAsync()).Any(l => EstadoBridge.EstaVivo(l, DateTime.UtcNow)), "El Bridge no está corriendo.");

        var origen = Environment.GetEnvironmentVariable("PSAWEB_TEST_COMPRA_MIXTA_OC") ?? "OC-8021";
        await using var cn = new OdbcConnection(EntornoCompras.CadenaSage);
        await cn.OpenAsync();
        await using var pe = EntornoCompras.PeachEbills();
        var catalogo = await LectorCatalogoCompras.LeerAsync(cn, await LectorCatalogoPeachEbills.LeerAsync(pe));
        var poOrigen = Entero(Escalar(cn, "SELECT PostOrder FROM JrnlHdr WHERE JrnlKey_Journal = 10 AND JournalEx = 18 AND Reference = ?", origen));
        var compraWorker = Entero(Escalar(cn, "SELECT PostOrder FROM JrnlHdr WHERE JrnlKey_Journal = 4 AND INV_POSOOrderNumber = ? ORDER BY PostOrder DESC", origen));

        // «Copiar» como la web, con otro nº de factura.
        var oc = (await LectorOcs.LeerAsync(cn, poOrigen))!;
        var proveedor = (await LectorCatalogoCompras.ProveedorPorIdAsync(cn, oc.Cabecera.VendorId))!;
        var armado = ArmadorOc.Armar(RecargaOc.Reconstruir(oc, proveedor, catalogo, EntornoCompras.Ruc).Entrada, catalogo);
        Assert.True(armado.Oc is not null, string.Join(" | ", armado.Errores));
        var solicitud = SolicitudGuardarOc.Crear(armado.Oc!, proveedor, numeroOcAutomatico: true, numeroRetencionAutomatico: true);
        var payload = JsonSerializer.Deserialize<PayloadGuardarOc>(solicitud.PayloadJson)!;
        payload.NumeroFactura = $"999-996-{DateTime.Now:HHmmssfff}";

        var informe = new StringBuilder($"Compra mixta: {origen} (OC {poOrigen}, compra del worker {compraWorker}) → factura {payload.NumeroFactura}\n");
        var fallas = new List<string>();
        var previa = (await cola.EmpresasAsync()).FirstOrDefault(e => e.Ruc == EntornoCompras.Ruc);
        await cola.GuardarEmpresaAsync(EntornoCompras.Ruc, true, previa?.Ventana, "Compra mixta (arnés)", "test-mixta");
        try
        {
            var t1 = await CorrerAsync(cola, TiposTrabajo.GuardarOc, JsonSerializer.Serialize(payload), informe);
            Assert.Equal(EstadosTrabajo.Hecho, t1.Estado);
            var ocNueva = JsonSerializer.Deserialize<ResultadoGuardarOc>(t1.ResultadoJson!)!;
            var t2 = await CorrerAsync(cola, TiposTrabajo.ConvertirOcs, JsonSerializer.Serialize(new PayloadConvertirOcs { PostOrders = [ocNueva.PostOrder] }), informe);
            Assert.Equal(EstadosTrabajo.Hecho, t2.Estado);
            // El Bridge encola su propia conversión al terminar GuardarOc (ConvertirAlGuardar): la compra puede venir de ahí.
            var compra = Entero(Escalar(cn, "SELECT PostOrder FROM JrnlHdr WHERE JrnlKey_Journal = 4 AND JournalEx = 11 AND INV_POSOOrderNumber = ? ORDER BY PostOrder DESC", ocNueva.NumeroOc));
            Assert.True(compra > 0, $"No se encontró la compra de {ocNueva.NumeroOc}: {t2.ResultadoJson}");

            // Vínculo con la OC y OC cerrada.
            var vinculo = T(Escalar(cn, $"SELECT INV_POSOOrderNumber FROM JrnlHdr WHERE PostOrder = {compra}", null));
            if (vinculo != ocNueva.NumeroOc) fallas.Add($"La compra no quedó vinculada: «{vinculo}» / {ocNueva.NumeroOc}");
            if (Entero(Escalar(cn, $"SELECT POSOisClosed FROM JrnlHdr WHERE PostOrder = {ocNueva.PostOrder}", null)) != 1) fallas.Add("La OC no quedó cerrada.");

            // Filas: mismas cuentas, cantidades y montos que la del worker (sin los enlaces por fila); inventario marcado.
            const string sqlFilas = "SELECT r.RowNumber, r.RowType, r.GLAcntNumber, r.ItemRecordNumber, r.Amount, r.IncludeInInvLedger, l.ItemClass " +
                                    "FROM JrnlRow r, LineItem l WHERE r.ItemRecordNumber = l.ItemRecordNumber AND r.PostOrder = {0} ORDER BY r.RowNumber, r.RowType, r.GLAcntNumber";
            var fw = Tabla(cn, string.Format(CultureInfo.InvariantCulture, sqlFilas, compraWorker));
            var fb = Tabla(cn, string.Format(CultureInfo.InvariantCulture, sqlFilas, compra));
            if (fw.Rows.Count != fb.Rows.Count) fallas.Add($"Filas con ítem: worker {fw.Rows.Count} / Bridge {fb.Rows.Count}");
            var stock = 0;
            for (var i = 0; i < Math.Min(fw.Rows.Count, fb.Rows.Count); i++)
            {
                var w = fw.Rows[i];
                var b = fb.Rows[i];
                foreach (var col in new[] { "RowNumber", "RowType", "GLAcntNumber", "ItemRecordNumber", "Amount" })
                {
                    if (T(w[col]) != T(b[col])) fallas.Add($"Fila {i} {col}: worker {T(w[col])} / Bridge {T(b[col])}");
                }
                if (Entero(b["ItemClass"]) == 1 && Entero(b["RowType"]) == 0)
                {
                    stock++;
                    if (Entero(b["IncludeInInvLedger"]) != 1) fallas.Add($"Fila {i} (inventario) sin IncludeInInvLedger.");
                }
            }
            if (stock == 0) fallas.Add("La OC de origen no tiene ítems de inventario: elige otra.");

            // Capas de costo.
            const string sqlCapas = "SELECT l.ItemID, i.Quantity, i.TransAmount FROM InventoryCosts i, LineItem l WHERE i.ItemRecNumber = l.ItemRecordNumber " +
                                    "AND i.MajorType = 1 AND i.PostOrderNumber = {0} ORDER BY l.ItemID";
            var cw = string.Join(";", Tabla(cn, string.Format(CultureInfo.InvariantCulture, sqlCapas, compraWorker)).Rows.Cast<DataRow>().Select(r => string.Join("|", r.ItemArray.Select(T))));
            var cb = string.Join(";", Tabla(cn, string.Format(CultureInfo.InvariantCulture, sqlCapas, compra)).Rows.Cast<DataRow>().Select(r => string.Join("|", r.ItemArray.Select(T))));
            if (cw != cb) fallas.Add($"Capas de costo: worker {cw} / Bridge {cb}");
            informe.AppendLine($"OC {ocNueva.NumeroOc} ({ocNueva.PostOrder}) → compra {compra}; {stock} filas de inventario; capas {cb}");
        }
        finally
        {
            await cola.GuardarEmpresaAsync(EntornoCompras.Ruc, previa?.Habilitada ?? false, previa?.Ventana, previa?.Nota ?? "Compra mixta terminada", "test-mixta");
        }

        foreach (var f in fallas) informe.AppendLine(" ! " + f);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "psa-compra-mixta.txt"), informe.ToString());
        salida.WriteLine(informe.ToString());
        Assert.True(fallas.Count == 0, informe.ToString());
    }

    private static async Task<TrabajoSage> CorrerAsync(ColaSage cola, string tipo, string json, StringBuilder informe)
    {
        var id = (await cola.EncolarAsync(EntornoCompras.Ruc, tipo, json, $"test-mixta-{tipo}-{DateTime.UtcNow:yyyyMMddHHmmssfff}", "test-mixta")).Trabajo.Id;
        var limite = DateTime.UtcNow.AddMinutes(10);
        TrabajoSage t;
        while (true)
        {
            t = (await cola.ObtenerAsync(id))!;
            if (EstadosTrabajo.EsFinal(t.Estado) || DateTime.UtcNow > limite) break;
            await Task.Delay(2000);
        }
        informe.AppendLine($"Trabajo #{id} {tipo}: {t.Estado} {t.Error} {t.ResultadoJson}");
        return t;
    }

    private static object? Escalar(OdbcConnection cn, string sql, string? parametro)
    {
        using var cmd = new OdbcCommand(sql, cn);
        if (parametro is not null) cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = parametro });
        return cmd.ExecuteScalar();
    }

    private static DataTable Tabla(OdbcConnection cn, string sql)
    {
        var dt = new DataTable();
        using var da = new OdbcDataAdapter(sql, cn);
        da.Fill(dt);
        return dt;
    }

    private static int Entero(object? v) => v is null or DBNull ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture);

    private static string T(object? v) => v is null or DBNull ? string.Empty : Convert.ToString(v, CultureInfo.InvariantCulture)!.Trim();
}
