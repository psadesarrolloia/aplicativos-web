using System.Data.Common;
using System.Data.Odbc;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PsaWeb.Compras.Armado;
using PsaWeb.Compras.Catalogo;
using PsaWeb.Compras.Sri;
using PsaWeb.PeachEbills.Data;
using Xunit.Abstractions;

namespace PsaWeb.Compras.Tests.Validacion;

/// <summary>
/// Cierre de la F2 (§10 del plan de la Ola 2): reconstruye, desde el XML del SRI, las OC que el `.exe` registró en la
/// copia de prueba y las compara fila a fila con Sage. SOLO LECTURA (ODBC SELECT + PeachEBills local).
/// <para>
/// Lo que el digitador eligió en el formulario (forma de pago, cuenta y retenciones de cada línea) no está en el XML: se
/// deduce de la propia OC (cuenta de cada fila; retención de renta por el ítem C usado y la suma de bases; retención de
/// IVA por la base). Lo que se valida es todo lo que calcula la lógica: ítem C, IVA, bases y montos de retención,
/// precios, orden de las líneas, AUT-SRI, cabecera.
/// </para>
/// Variables (datos reales: nada de esto se commitea):
/// <list type="bullet">
///   <item><c>PSAWEB_TEST_SAGE_COMPRAS</c> — cadena ODBC de la copia de prueba (lleva la clave).</item>
///   <item><c>PSAWEB_TEST_XML_COMPRAS</c> — carpeta con <c>&lt;clave&gt;.xml</c> (por defecto <c>C:\PSA-F2\xml</c>).</item>
///   <item><c>PSAWEB_TEST_RUC_COMPRAS</c> — RUC de la empresa (por defecto CPTDC).</item>
/// </list>
/// </summary>
public class ReconstruccionOcRealesTests(ITestOutputHelper salida)
{
    private const string PeachEbillsLocal =
        @"Server=.\SQLEXPRESS;Database=PeachEBills;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15";

    private static readonly string? CadenaSage = Environment.GetEnvironmentVariable("PSAWEB_TEST_SAGE_COMPRAS");
    private static readonly string CarpetaXml = Environment.GetEnvironmentVariable("PSAWEB_TEST_XML_COMPRAS") ?? @"C:\PSA-F2\xml";
    private static readonly string Ruc = Environment.GetEnvironmentVariable("PSAWEB_TEST_RUC_COMPRAS") ?? "1792051800001";

    private readonly List<string> MontosEditados = [];
    private int Resumidas;
    private int DescripcionesEditadas;

    private sealed record Cabecera(int PostOrder, string Referencia, DateTime Fecha, DateTime FechaRegistro, string ShipVia,
        string Terminos, string Direccion1, string Direccion2, string Estado, string Zip, string VendorId);

    private sealed record Fila(int Numero, string ItemId, string Categoria, string Descripcion, decimal Cantidad,
        decimal PrecioUnitario, decimal Monto, string Cuenta, string Job);

    [SkippableFact]
    public async Task Las_oc_reales_se_reconstruyen_desde_su_xml_sin_diferencias()
    {
        Skip.If(CadenaSage is null, "Sin PSAWEB_TEST_SAGE_COMPRAS (cadena ODBC de la copia de prueba).");
        Skip.IfNot(Directory.Exists(CarpetaXml), $"No existe {CarpetaXml}.");
        Skip.IfNot(Environment.Is64BitProcess == false, "El driver ODBC de Pervasive es de 32 bits.");

        await using var pe = new PeachEbillsContext(new DbContextOptionsBuilder<PeachEbillsContext>().UseSqlServer(PeachEbillsLocal).Options);
        Skip.IfNot(await pe.Database.CanConnectAsync(), "PeachEBills local no disponible.");

        await using var cn = new OdbcConnection(CadenaSage);
        await cn.OpenAsync();
        var catalogo = await LectorCatalogoCompras.LeerAsync(cn, await LectorCatalogoPeachEbills.LeerAsync(pe));
        var ocs = await LeerOcsAsync(cn);

        var informe = new StringBuilder();
        int comparadas = 0, conDiferencias = 0, sinXml = 0;
        foreach (var (cab, filas) in ocs)
        {
            if (cab.Terminos.StartsWith("999-999-")) continue; // OC de prueba creada por SDK en la F0 (§14.3)
            var clave = filas.First(f => f.ItemId == "AUT-SRI").Descripcion;
            var ruta = Path.Combine(CarpetaXml, clave + ".xml");
            if (!File.Exists(ruta)) { sinXml++; continue; }
            comparadas++;
            var difs = await CompararAsync(cn, pe, catalogo, cab, filas, File.ReadAllText(ruta));
            if (difs.Count == 0) continue;
            conDiferencias++;
            informe.AppendLine($"== {cab.Referencia} (PostOrder {cab.PostOrder}, {cab.VendorId})");
            foreach (var d in difs) informe.AppendLine("   " + d);
        }

        var resumen = $"OC comparadas: {comparadas} · con diferencias: {conDiferencias} · sin XML: {sinXml} · " +
                      $"resumidas: {Resumidas} · descripciones editadas por el digitador: {DescripcionesEditadas} · " +
                      $"con monto cambiado a mano (no cuadran con la factura): {MontosEditados.Count}";
        foreach (var m in MontosEditados) informe.AppendLine("   Monto editado — " + m);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "psa-f2-comparacion.txt"), resumen + Environment.NewLine + informe);
        salida.WriteLine(resumen);
        salida.WriteLine(informe.ToString());
        Assert.True(comparadas > 0, "No se encontró ninguna OC con su XML.");
        Assert.True(conDiferencias == 0, resumen + Environment.NewLine + informe);
    }

    private async Task<List<string>> CompararAsync(OdbcConnection cn, PeachEbillsContext pe, CatalogoCompras catalogo,
        Cabecera cab, List<Fila> filas, string xml)
    {
        var difs = new List<string>();
        var lectura = LectorFacturaSri.Leer(xml);
        if (lectura.Factura is not { } factura) return [$"XML: {lectura.Error}"];
        var proveedor = await LectorCatalogoCompras.ProveedorPorIdAsync(cn, cab.VendorId);
        if (proveedor is null) return ["Proveedor no encontrado"];
        var configs = await LectorCatalogoPeachEbills.ConfiguracionesAsync(pe, Ruc, factura.Emisor.Ruc);

        // --- Lo que el `.exe` precarga ---
        var detalles = PreparacionCompra.Detalles(factura, proveedor.CuentaGasto, configs, catalogo);
        var (impuestos, erroresImp) = PreparacionCompra.Impuestos(factura, proveedor.CuentaGasto, catalogo);
        difs.AddRange(erroresImp.Select(e => "Impuestos: " + e));

        // --- Lo que eligió el digitador, deducido de la OC ---
        var iAut = filas.FindIndex(f => f.ItemId == "AUT-SRI");
        var nDetalle = iAut - impuestos.Count - (factura.Totales.Propina > 0 ? 1 : 0);
        // ¿Pulsó «Resumir detalles»? Se elige la variante cuyas cantidades y montos calzan con la OC.
        bool Calza(List<LineaDetalle> d) => d.Count == nDetalle
            && d.Select((x, i) => x.Cantidad == filas[i].Cantidad && x.MontoSinImpuestos == filas[i].Monto).All(b => b);
        var resumidos = PreparacionCompra.Resumir(detalles, proveedor.CuentaGasto);
        if (!Calza(detalles) && Calza(resumidos)) { detalles = resumidos; Resumidas++; }
        else if (detalles.Count != nDetalle && resumidos.Count == nDetalle)
        {
            // Resumió y después cambió un monto a mano: la OC ya no cuadra con la factura; no hay nada que reconstruir.
            MontosEditados.Add($"{cab.Referencia}: detalles Sage [{string.Join("; ", filas.Take(nDetalle).Select(f => f.Monto))}] " +
                               $"/ resumidos del XML [{string.Join("; ", resumidos.Select(x => x.MontoSinImpuestos))}]");
            return [];
        }
        else if (detalles.Count != nDetalle)
        {
            return [$"Estructura: la OC tiene {nDetalle} líneas de detalle; el XML {detalles.Count} ({resumidos.Count} resumidas)"];
        }
        var itemsC = catalogo.ItemsC.ToDictionary(x => x.Id);
        for (var i = 0; i < detalles.Count; i++)
        {
            // La descripción es editable en la grilla del `.exe`: es dato del digitador (se cuenta aparte).
            if (detalles[i].Descripcion.Trim() != filas[i].Descripcion) { DescripcionesEditadas++; detalles[i].Descripcion = filas[i].Descripcion; }
            detalles[i].CuentaId = filas[i].Cuenta;
            detalles[i].JobId = filas[i].Job.Length == 0 ? null : filas[i].Job;
            detalles[i].ItemId = itemsC.ContainsKey(filas[i].ItemId) ? null : filas[i].ItemId;
        }
        var posteriores = filas.Skip(iAut + 1).ToList();
        var filasRf = posteriores.Where(f => f.Categoria == "R-IRF").ToList();
        var filasRiva = posteriores.Where(f => f.Categoria == "R-IVA").ToList();
        var asumidas = posteriores.Any(f => f.ItemId.Length == 0 && f.Descripcion != ".");
        var formaPago = DeducirFormaPago(filasRf, filasRiva, asumidas, catalogo);
        if (!AsignarRetencionFuente(detalles, filas, filasRf, itemsC, catalogo)) difs.Add("No se pudo deducir la retención de renta de cada línea");
        if (!AsignarRetencionIva(detalles, filasRiva)) difs.Add("No se pudo deducir la retención de IVA de cada línea");

        var entrada = new EntradaCompra
        {
            RucEmpresa = Ruc,
            TipoDocumento = cab.ShipVia switch { "NOTA DE VENTA" => TipoDocumentoCompra.NotaDeVenta, "LIQUIDACION" => TipoDocumentoCompra.Liquidacion, _ => TipoDocumentoCompra.Factura },
            Sustento = cab.Estado == "01" ? SustentoCompra.Credito : SustentoCompra.Costo,
            Origen = PreparacionCompra.Origen(factura, Ruc),
            Factura = factura,
            NumeroFactura = factura.NumeroCompleto,
            Autorizacion = factura.ClaveAcceso,
            FechaEmision = factura.FechaEmision,
            FechaRegistro = cab.FechaRegistro,
            Proveedor = proveedor,
            Detalles = detalles,
            Impuestos = impuestos,
            Propina = factura.Totales.Propina,
            FormaPagoId = formaPago,
            Retenciones = CalculadorRetenciones.Calcular(detalles, formaPago, proveedor.CuentaGasto, catalogo),
            NumeroRetencion = cab.Direccion2.Length == 0 ? null : cab.Direccion2,
            NumeroOc = cab.Referencia,
        };
        var r = ArmadorOc.Armar(entrada, catalogo);
        if (r.Oc is not { } oc) return [.. difs, .. r.Errores.Select(e => "Validación: " + e)];

        // --- Cabecera ---
        void Cab(string campo, string? esperado, string real)
        {
            if ((esperado ?? string.Empty) != real) difs.Add($"{campo}: web «{esperado}» / Sage «{real}»");
        }
        Cab("ShipVia", oc.ShipVia, cab.ShipVia);
        Cab("TermsDescription", oc.NumeroFactura, cab.Terminos);
        Cab("ShipToAddress1", oc.NumeroFactura, cab.Direccion1);
        Cab("ShipToAddress2", oc.NumeroRetencion, cab.Direccion2);
        Cab("ShipToState", oc.EstadoSustento, cab.Estado);
        Cab("ShipToZIP", oc.Zip, cab.Zip);
        Cab("Fecha", oc.Fecha.ToString("yyyy-MM-dd"), cab.Fecha.ToString("yyyy-MM-dd"));
        Cab("GoodThruDate", oc.FechaRegistro.ToString("yyyy-MM-dd"), cab.FechaRegistro.ToString("yyyy-MM-dd"));

        // --- Líneas ---
        if (oc.Lineas.Count != filas.Count) difs.Add($"Líneas: web {oc.Lineas.Count} / Sage {filas.Count}");
        for (var i = 0; i < Math.Min(oc.Lineas.Count, filas.Count); i++)
        {
            var w = oc.Lineas[i];
            var s = filas[i];
            var cuentaWeb = w.CuentaId ?? proveedor.CuentaGasto; // «.»: Sage pone la cuenta del proveedor
            var desc = w.Descripcion.Trim();
            if ((w.ItemId ?? string.Empty) != s.ItemId || desc != s.Descripcion || w.Cantidad != s.Cantidad
                || w.Monto != s.Monto || Math.Round(w.PrecioUnitario, 15) != Math.Round(s.PrecioUnitario, 15)
                || cuentaWeb != s.Cuenta || (w.JobId ?? string.Empty) != s.Job)
            {
                difs.Add(FormattableString.Invariant(
                    $"Fila {s.Numero} ({w.Tipo}): web [{w.ItemId}|{desc}|{w.Cantidad}|{w.PrecioUnitario:0.###############}|{w.Monto}|{cuentaWeb}|{w.JobId}] / Sage [{s.ItemId}|{s.Descripcion}|{s.Cantidad}|{s.PrecioUnitario:0.###############}|{s.Monto}|{s.Cuenta}|{s.Job}]"));
            }
        }
        return difs;
    }

    private static int DeducirFormaPago(List<Fila> rf, List<Fila> riva, bool asumidas, CatalogoCompras catalogo)
    {
        if (asumidas) return FormaPagoRetencion.RetencionAsumida;
        var codigos = rf.Select(f => catalogo.RetencionFuente(f.ItemId)?.CustomField1 ?? string.Empty).ToList();
        if (riva.Count == 0 && codigos.Count > 0 && codigos.All(c => c.StartsWith("332")))
        {
            return codigos[0] switch { "332G" => FormaPagoRetencion.TarjetaCredito, "332I" => FormaPagoRetencion.DebitoAutorizado, _ => 6 };
        }
        return FormaPagoRetencion.Otros;
    }

    /// <summary>Línea con ítem C «RF: NO» → el R-IRF 332* de su <c>CustomField5</c>; el resto se reparte por suma de bases.</summary>
    private static bool AsignarRetencionFuente(List<LineaDetalle> detalles, List<Fila> filas, List<Fila> filasRf,
        Dictionary<string, ItemSage> itemsC, CatalogoCompras catalogo)
    {
        var libres = new List<int>();
        for (var i = 0; i < detalles.Count; i++)
        {
            if (itemsC.TryGetValue(filas[i].ItemId, out var c) && c.CustomField2.Contains("NO"))
            {
                detalles[i].RetencionFuenteId = filasRf.FirstOrDefault(f => catalogo.RetencionFuente(f.ItemId)?.CustomField1 == c.CustomField5)?.ItemId;
                if (detalles[i].RetencionFuenteId is null) return false;
            }
            else libres.Add(i);
        }
        var pendientes = filasRf.Select(f => (f.ItemId, Base: f.Cantidad - detalles.Where(d => d.RetencionFuenteId == f.ItemId).Sum(d => d.MontoSinImpuestos)))
            .Where(x => x.Base != 0).ToList();
        foreach (var (item, baseImponible) in pendientes)
        {
            var subconjunto = SubconjuntoConSuma(libres.Select(i => detalles[i].MontoSinImpuestos).ToList(), s => s == baseImponible);
            if (subconjunto is null) return false;
            foreach (var k in subconjunto.OrderByDescending(k => k)) { detalles[libres[k]].RetencionFuenteId = item; libres.RemoveAt(k); }
        }
        return true;
    }

    /// <summary>Cada R-IVA toma las líneas con IVA cuya suma × tarifa (redondeada) da su base.</summary>
    private static bool AsignarRetencionIva(List<LineaDetalle> detalles, List<Fila> filasRiva)
    {
        var libres = Enumerable.Range(0, detalles.Count).Where(i => detalles[i].TarifaIva > 0).ToList();
        foreach (var f in filasRiva)
        {
            var hallado = false;
            foreach (var tarifa in libres.Select(i => detalles[i].TarifaIva).Distinct().ToList())
            {
                var tasa = tarifa >= 1 ? tarifa / 100m : tarifa;
                var candidatos = libres.Where(i => detalles[i].TarifaIva == tarifa).ToList();
                var subconjunto = SubconjuntoConSuma(candidatos.Select(i => detalles[i].MontoSinImpuestos).ToList(),
                    s => Math.Round(s * tasa, 2, MidpointRounding.AwayFromZero) == f.Cantidad);
                if (subconjunto is null) continue;
                foreach (var k in subconjunto) detalles[candidatos[k]].RetencionIvaId = f.ItemId;
                libres.RemoveAll(i => detalles[i].RetencionIvaId is not null);
                hallado = true;
                break;
            }
            if (!hallado) return false;
        }
        return true;
    }

    /// <summary>Índices de un subconjunto cuya suma cumple <paramref name="cumple"/>; prueba primero «todos».</summary>
    private static List<int>? SubconjuntoConSuma(List<decimal> montos, Func<decimal, bool> cumple)
    {
        if (montos.Count > 0 && cumple(montos.Sum())) return Enumerable.Range(0, montos.Count).ToList();
        var alcanzables = new Dictionary<decimal, List<int>> { [0m] = [] };
        for (var i = 0; i < montos.Count; i++)
        {
            foreach (var (suma, indices) in alcanzables.ToList())
            {
                var nueva = suma + montos[i];
                if (!alcanzables.ContainsKey(nueva)) alcanzables[nueva] = [.. indices, i];
            }
            if (alcanzables.Count > 200_000) break;
        }
        return alcanzables.Where(x => x.Value.Count > 0 && cumple(x.Key)).Select(x => x.Value).FirstOrDefault();
    }

    private static async Task<List<(Cabecera, List<Fila>)>> LeerOcsAsync(OdbcConnection cn)
    {
        const string sqlCab = """
            SELECT h.PostOrder, h.Reference, h.TransactionDate, h.GoodThruDate, h.ShipVia, h.TermsDescription,
                   h.ShipToAddress1, h.ShipToAddress2, h.ShipToState, h.ShipToZIP, v.VendorID
            FROM JrnlHdr h, Vendors v
            WHERE h.CustVendId = v.VendorRecordNumber AND h.JrnlKey_Journal = 10 AND h.JournalEx = 18
              AND h.TransactionDate >= { d '2026-09-11' }
              AND h.PostOrder IN (SELECT r.PostOrder FROM JrnlRow r, LineItem l WHERE r.ItemRecordNumber = l.ItemRecordNumber AND l.ItemID = 'AUT-SRI')
            ORDER BY h.PostOrder
            """;
        const string sqlFilas = """
            SELECT r.PostOrder, r.RowNumber, l.ItemID, l.Category, r.RowDescription, r.Quantity, r.UnitCost, r.Amount, c.AccountID, j.JobID
            FROM JrnlRow r LEFT OUTER JOIN LineItem l ON r.ItemRecordNumber = l.ItemRecordNumber
                 LEFT OUTER JOIN Chart c ON r.GLAcntNumber = c.GLAcntNumber
                 LEFT OUTER JOIN Jobs j ON r.JobRecordNumber = j.JobRecordNumber
            WHERE r.RowNumber > 0 AND r.PostOrder IN (
                SELECT h.PostOrder FROM JrnlHdr h WHERE h.JrnlKey_Journal = 10 AND h.JournalEx = 18 AND h.TransactionDate >= { d '2026-09-11' })
            ORDER BY r.PostOrder, r.RowNumber
            """;
        var cabeceras = new List<Cabecera>();
        await using (var cmd = new OdbcCommand(sqlCab, cn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                cabeceras.Add(new Cabecera(Convert.ToInt32(r["PostOrder"]), T(r, "Reference"), Convert.ToDateTime(r["TransactionDate"]),
                    Convert.ToDateTime(r["GoodThruDate"]), T(r, "ShipVia"), T(r, "TermsDescription"), T(r, "ShipToAddress1"),
                    T(r, "ShipToAddress2"), T(r, "ShipToState"), T(r, "ShipToZIP"), T(r, "VendorID")));
            }
        }
        var filas = new Dictionary<int, List<Fila>>();
        await using (var cmd = new OdbcCommand(sqlFilas, cn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var po = Convert.ToInt32(r["PostOrder"]);
                if (!filas.TryGetValue(po, out var lista)) filas[po] = lista = [];
                lista.Add(new Fila(Convert.ToInt32(r["RowNumber"]), T(r, "ItemID"), T(r, "Category"), T(r, "RowDescription"),
                    D(r, "Quantity"), D(r, "UnitCost"), D(r, "Amount"), T(r, "AccountID"), T(r, "JobID")));
            }
        }
        return cabeceras.Where(c => filas.ContainsKey(c.PostOrder)).Select(c => (c, filas[c.PostOrder])).ToList();
    }

    private static string T(DbDataReader r, string col) => r[col] is DBNull ? string.Empty : Convert.ToString(r[col], CultureInfo.InvariantCulture)!.Trim();
    private static decimal D(DbDataReader r, string col) => r[col] is DBNull ? 0m : Convert.ToDecimal(r[col]);
}
