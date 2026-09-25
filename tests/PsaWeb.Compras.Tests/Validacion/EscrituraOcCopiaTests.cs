using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PsaWeb.Compras.Armado;
using PsaWeb.Compras.Bridge;
using PsaWeb.Compras.Catalogo;
using PsaWeb.SageBridge.Cola;
using PsaWeb.SageBridge.Cola.Data;
using PsaWeb.SageBridge.Contratos;
using Xunit.Abstractions;

namespace PsaWeb.Compras.Tests.Validacion;

/// <summary>
/// Cierre de la F3 (§10 del plan de la Ola 2): <b>ESCRIBE en la copia de prueba</b> por el Sage Bridge. Para una muestra de
/// OC reales (una por perfil: forma de pago, retenciones, resumida, asumida…) arma la OC desde el XML con un nº de factura de
/// prueba (<c>999-998-&lt;PostOrder&gt;</c>), la encola como <see cref="TiposTrabajo.GuardarOc"/>, espera al Bridge y compara
/// por ODBC <b>todas</b> las columnas de <c>JrnlHdr</c>/<c>JrnlRow</c> con la OC que grabó el `.exe`. Después prueba la
/// actualización en el lugar y dos rechazos (nº de OC y nº de retención ya usados).
/// <para>
/// Solo corre con <c>PSAWEB_TEST_F3_ESCRIBIR=1</c>, las variables de <see cref="EntornoCompras"/> y el Bridge en consola con la
/// configuración de desarrollo (<c>SoloBases</c> = la copia de prueba). Habilita la empresa en el Bridge mientras dura y la
/// deshabilita al final.
/// </para>
/// </summary>
public class EscrituraOcCopiaTests(ITestOutputHelper salida)
{
    /// <summary>
    /// Columnas que difieren por diseño entre la OC del `.exe` y la de prueba: identidad y fechas de grabado, los datos de
    /// prueba (nº de OC, factura, retención), el estado de recepción (las del `.exe` ya se convirtieron en compra) y lo visto
    /// en la F0-b (§14.3).
    /// </summary>
    private static readonly HashSet<string> ColumnasEsperadas = new(StringComparer.OrdinalIgnoreCase)
    {
        // Datos de prueba: nº de OC, factura 999-998-…, nº de retención nuevo.
        "PostOrder", "Reference", "TermsDescription", "ShipToAddress1", "ShipToAddress2",
        // Identidad y grabado.
        "JrnlKey_TrxNumber", "LastPostedAt", "LastUpdateCounter", "rGUIDa", "rGUIDb", "rGUIDc", "rGUIDd",
        // Recepción: las OC del `.exe` ya las convirtió en compra el worker; las de prueba siguen abiertas.
        "POSOIsClosed", "QtyReceived", "StockingQtyReceived", "AmountReceived",
    };

    private sealed class FabricaCola : IDbContextFactory<SageBridgeDbContext>
    {
        public SageBridgeDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<SageBridgeDbContext>().UseSqlServer(EntornoCompras.PlataformaLocal).Options);
    }

    [SkippableFact]
    public async Task Las_oc_escritas_por_el_bridge_son_iguales_a_las_del_exe()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("PSAWEB_TEST_F3_ESCRIBIR") == "1", "Escribe en la copia de prueba: solo con PSAWEB_TEST_F3_ESCRIBIR=1.");
        Skip.If(EntornoCompras.CadenaSage is null, "Sin PSAWEB_TEST_SAGE_COMPRAS.");
        Skip.If(Environment.Is64BitProcess, "El driver ODBC de Pervasive es de 32 bits.");
        var cola = new ColaSage(new FabricaCola());
        var latidos = await cola.LatidosAsync();
        Skip.IfNot(latidos.Any(l => EstadoBridge.EstaVivo(l, DateTime.UtcNow)), "El Bridge no está corriendo (PsaWeb.SageBridge.exe --consola).");

        await using var pe = EntornoCompras.PeachEbills();
        await using var cn = new OdbcConnection(EntornoCompras.CadenaSage);
        await cn.OpenAsync();
        var catalogo = await LectorCatalogoCompras.LeerAsync(cn, await LectorCatalogoPeachEbills.LeerAsync(pe));

        // ---- Muestra: una OC reconstruible por perfil ----
        var muestra = new List<(ReconstructorOc.Cabecera Cab, EntradaCompra Entrada, ProveedorSage Proveedor)>();
        var perfiles = new HashSet<string>();
        foreach (var (cab, filas) in await ReconstructorOc.LeerOcsAsync(cn))
        {
            var ruta = Path.Combine(EntornoCompras.CarpetaXml, filas.First(f => f.ItemId == "AUT-SRI").Descripcion + ".xml");
            if (!File.Exists(ruta)) continue;
            var r = await ReconstructorOc.ReconstruirAsync(cn, pe, catalogo, cab, filas, File.ReadAllText(ruta));
            if (r.Entrada is null || r.Errores.Count > 0) continue;
            var e = r.Entrada;
            var perfil = $"{e.FormaPagoId}|{e.Detalles.Any(d => d.RetencionIvaId != null)}|{r.Resumida}|{e.Detalles.Count > 3}|{e.Propina > 0}|{e.Impuestos.Count}";
            if (perfiles.Add(perfil)) muestra.Add((cab, e, r.Proveedor!));
        }
        salida.WriteLine($"Muestra: {muestra.Count} OC ({string.Join(", ", muestra.Select(m => m.Cab.Referencia))})");
        Assert.NotEmpty(muestra);

        await cola.GuardarEmpresaAsync(EntornoCompras.Ruc, true, null, "Validación F3 (arnés)", "test-f3");
        var informe = new StringBuilder();
        var fallas = 0;
        try
        {
            // ---- 1. Crear ----
            var creadas = new List<(ReconstructorOc.Cabecera Cab, EntradaCompra Entrada, ProveedorSage Proveedor, ResultadoGuardarOc Resultado)>();
            var trabajos = new List<(long Id, int Indice)>();
            for (var i = 0; i < muestra.Count; i++)
            {
                var (cab, entrada, proveedor) = muestra[i];
                var prueba = ConFactura(entrada, $"999-998-{cab.PostOrder:000000000}");
                trabajos.Add((await EncolarAsync(cola, prueba, proveedor, catalogo), i));
            }
            foreach (var (id, i) in trabajos)
            {
                var t = await EsperarAsync(cola, id);
                if (t.Estado != EstadosTrabajo.Hecho) { fallas++; informe.AppendLine($"== {muestra[i].Cab.Referencia}: {t.Estado} — {t.Error}"); continue; }
                var res = JsonSerializer.Deserialize<ResultadoGuardarOc>(t.ResultadoJson!)!;
                creadas.Add((muestra[i].Cab, muestra[i].Entrada, muestra[i].Proveedor, res));
                var difs = CompararColumnas(cn, muestra[i].Cab.PostOrder, res.PostOrder);
                informe.AppendLine($"== {muestra[i].Cab.Referencia} → {res.NumeroOc} (PostOrder {res.PostOrder}, {res.Accion}, retención {res.NumeroRetencion}, proveedor {res.Proveedor}): {difs.Count} diferencias inesperadas");
                foreach (var d in difs) informe.AppendLine("   " + d);
                if (difs.Count > 0) fallas++;
            }

            // ---- 2. Actualizar en el lugar: mismo PostOrder, mismo nº de OC y de retención ----
            foreach (var (cab, entrada, proveedor, antes) in creadas.Take(2))
            {
                var detalles = entrada.Detalles.Select(d => d.Copiar()).ToList();
                detalles[0].Descripcion = "PRUEBA F3 ACTUALIZADA";
                var cambiada = ConFactura(entrada, $"999-998-{cab.PostOrder:000000000}", detalles);
                var t = await EsperarAsync(cola, await EncolarAsync(cola, cambiada, proveedor, catalogo));
                var res = t.ResultadoJson is null ? null : JsonSerializer.Deserialize<ResultadoGuardarOc>(t.ResultadoJson);
                var ok = t.Estado == EstadosTrabajo.Hecho && res!.Accion == "Actualizada" && res.PostOrder == antes.PostOrder
                         && res.NumeroOc == antes.NumeroOc && res.NumeroRetencion == antes.NumeroRetencion
                         && PrimeraDescripcion(cn, res.PostOrder) == "PRUEBA F3 ACTUALIZADA";
                informe.AppendLine($"== Actualizar {antes.NumeroOc}: {(ok ? "OK" : "FALLA")} ({t.Estado} {t.Error} {res?.Accion} PO {res?.PostOrder} ret {res?.NumeroRetencion})");
                if (!ok) fallas++;
            }

            // ---- 3. Rechazos ----
            if (creadas.Count > 0)
            {
                var (cab, entrada, proveedor, antes) = creadas[0];
                var ocUsada = ConFactura(entrada, $"999-997-{cab.PostOrder:000000000}");
                var t1 = await EsperarAsync(cola, await EncolarAsync(cola, ocUsada, proveedor, catalogo, numeroOc: antes.NumeroOc));
                var r1 = t1.Estado == EstadosTrabajo.Error && t1.Error!.Contains("ya existe");
                informe.AppendLine($"== Rechazo nº de OC usado: {(r1 ? "OK" : "FALLA")} ({t1.Estado} {t1.Error})");
                var t2 = await EsperarAsync(cola, await EncolarAsync(cola, ocUsada, proveedor, catalogo, numeroRetencion: antes.NumeroRetencion));
                var r2 = string.IsNullOrEmpty(antes.NumeroRetencion) || (t2.Estado == EstadosTrabajo.Error && t2.Error!.Contains("ya está usado"));
                informe.AppendLine($"== Rechazo nº de retención usado: {(r2 ? "OK" : "FALLA")} ({t2.Estado} {t2.Error})");
                if (!r1) fallas++;
                if (!r2) fallas++;
            }
        }
        finally
        {
            await cola.GuardarEmpresaAsync(EntornoCompras.Ruc, false, null, "Validación F3 terminada", "test-f3");
        }

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "psa-f3-escritura.txt"), informe.ToString());
        salida.WriteLine(informe.ToString());
        Assert.True(fallas == 0, informe.ToString());
    }

    private static async Task<long> EncolarAsync(ColaSage cola, EntradaCompra entrada, ProveedorSage proveedor, CatalogoCompras catalogo,
        string? numeroOc = null, string? numeroRetencion = null)
    {
        var armado = ArmadorOc.Armar(entrada, catalogo);
        Assert.True(armado.Oc is not null, string.Join(" / ", armado.Errores));
        var oc = armado.Oc! with { Referencia = numeroOc ?? armado.Oc.Referencia, NumeroRetencion = numeroRetencion ?? armado.Oc.NumeroRetencion };
        var s = SolicitudGuardarOc.Crear(oc, proveedor, numeroOcAutomatico: numeroOc is null, numeroRetencionAutomatico: numeroRetencion is null);
        return (await cola.EncolarAsync(EntornoCompras.Ruc, s.Tipo, s.PayloadJson, s.ClaveIdempotencia, "test-f3")).Trabajo.Id;
    }

    private static async Task<TrabajoSage> EsperarAsync(ColaSage cola, long id)
    {
        var limite = DateTime.UtcNow.AddMinutes(15);
        while (true)
        {
            var t = (await cola.ObtenerAsync(id))!;
            if (EstadosTrabajo.EsFinal(t.Estado) || DateTime.UtcNow > limite) return t;
            await Task.Delay(2000);
        }
    }

    private static EntradaCompra ConFactura(EntradaCompra e, string numeroFactura, IReadOnlyList<LineaDetalle>? detalles = null) => new()
    {
        RucEmpresa = e.RucEmpresa, TipoDocumento = e.TipoDocumento, Sustento = e.Sustento, Origen = e.Origen, Factura = e.Factura,
        NumeroFactura = numeroFactura, Autorizacion = e.Autorizacion, FechaEmision = e.FechaEmision, FechaRegistro = e.FechaRegistro,
        Proveedor = e.Proveedor, Detalles = detalles ?? e.Detalles, Impuestos = e.Impuestos, Propina = e.Propina,
        FormaPagoId = e.FormaPagoId, Retenciones = e.Retenciones, NumeroRetencion = e.NumeroRetencion, NumeroOc = e.NumeroOc,
    };

    private static string PrimeraDescripcion(OdbcConnection cn, int postOrder)
    {
        using var cmd = new OdbcCommand($"SELECT RowDescription FROM JrnlRow WHERE PostOrder = {postOrder} AND RowNumber = 1", cn);
        return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    }

    /// <summary>Todas las columnas de <c>JrnlHdr</c> y de cada fila de <c>JrnlRow</c>, salvo <see cref="ColumnasEsperadas"/>.</summary>
    private static List<string> CompararColumnas(OdbcConnection cn, int original, int nueva)
    {
        DataTable Tabla(string sql)
        {
            var dt = new DataTable();
            using var da = new OdbcDataAdapter(sql, cn);
            da.Fill(dt);
            return dt;
        }
        string V(DataRow r, DataColumn c) => Convert.ToString(r[c], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

        var difs = new List<string>();
        var ha = Tabla($"SELECT * FROM JrnlHdr WHERE PostOrder = {original}");
        var hb = Tabla($"SELECT * FROM JrnlHdr WHERE PostOrder = {nueva}");
        if (hb.Rows.Count != 1) return [$"No se encontró la OC nueva (PostOrder {nueva})"];
        var actualizada = Convert.ToInt32(hb.Rows[0]["LastUsedDistNumber"]) > Convert.ToInt32(ha.Rows[0]["LastUsedDistNumber"]);
        foreach (DataColumn c in ha.Columns)
        {
            if (ColumnasEsperadas.Contains(c.ColumnName)) continue;
            if (c.ColumnName == "LastUsedDistNumber" && actualizada) continue;
            if (V(ha.Rows[0], c) != V(hb.Rows[0], hb.Columns[c.ColumnName]!)) difs.Add($"JrnlHdr.{c.ColumnName}: exe «{V(ha.Rows[0], c)}» / bridge «{V(hb.Rows[0], hb.Columns[c.ColumnName]!)}»");
        }
        var fa = Tabla($"SELECT * FROM JrnlRow WHERE PostOrder = {original} ORDER BY RowNumber");
        var fb = Tabla($"SELECT * FROM JrnlRow WHERE PostOrder = {nueva} ORDER BY RowNumber");
        if (fa.Rows.Count != fb.Rows.Count) difs.Add($"Filas: exe {fa.Rows.Count} / bridge {fb.Rows.Count}");
        for (var i = 0; i < Math.Min(fa.Rows.Count, fb.Rows.Count); i++)
        {
            foreach (DataColumn c in fa.Columns)
            {
                if (ColumnasEsperadas.Contains(c.ColumnName)) continue;
                var a = V(fa.Rows[i], c);
                var b = V(fb.Rows[i], fb.Columns[c.ColumnName]!);
                // Fila 0 (cuenta por pagar): el worker COM, al recibir la OC, deja el nombre del proveedor cortado a 30 (§14.3).
                if (i == 0 && c.ColumnName == "RowDescription" && a.Length == 30 && b.StartsWith(a, StringComparison.Ordinal)) continue;
                // Las dos OC que la fase 2 actualizó en el lugar (al volver a correr el arnés se comparan ya actualizadas):
                // descripción de prueba y distribuciones renumeradas (RemoveLine/AddLine siguen el contador de la OC).
                if (c.ColumnName == "RowDescription" && b == "PRUEBA F3 ACTUALIZADA") continue;
                if (c.ColumnName == "DistNumber" && actualizada) continue;
                if (a != b) difs.Add($"JrnlRow[{i}].{c.ColumnName}: exe «{a}» / bridge «{b}»");
            }
        }
        return difs;
    }
}
