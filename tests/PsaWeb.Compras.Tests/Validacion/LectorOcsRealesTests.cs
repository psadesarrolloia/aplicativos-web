using System.Data.Odbc;
using PsaWeb.Compras.Armado;
using PsaWeb.Compras.Sage;

namespace PsaWeb.Compras.Tests.Validacion;

/// <summary>Consultas del módulo Compras contra la copia de prueba (SOLO LECTURA). Variables en <see cref="EntornoCompras"/>.</summary>
public class LectorOcsRealesTests
{
    private static async Task<OdbcConnection> AbrirAsync()
    {
        Skip.If(EntornoCompras.CadenaSage is null, "Sin PSAWEB_TEST_SAGE_COMPRAS.");
        Skip.If(Environment.Is64BitProcess, "El driver ODBC de Pervasive es de 32 bits.");
        var cn = new OdbcConnection(EntornoCompras.CadenaSage);
        await cn.OpenAsync();
        return cn;
    }

    [SkippableFact]
    public async Task Lista_de_facturas_con_estado_y_autorizacion()
    {
        await using var cn = await AbrirAsync();

        var lista = await LectorOcs.ListarAsync(cn, new DateTime(2026, 9, 11), new DateTime(2026, 9, 23), TipoDocumentoCompra.Factura);

        Assert.True(lista.Count > 100);
        var oc = Assert.Single(lista, x => x.Referencia == "OC-8757");
        Assert.Equal(("001-008-001262988", "PETROPLATINUM CIA. LTDA.", EstadoOc.Contabilizado), (oc.Factura, oc.Proveedor.Trim().Length > 20 ? oc.Proveedor[..24] : oc.Proveedor, oc.Estado));
        Assert.True(oc.EsElectronica);
        Assert.Contains(lista, x => x.Estado == EstadoOc.Guardado); // las de prueba de la F3 (999-998-…) no se recibieron
    }

    [SkippableFact]
    public async Task Cuentas_jobs_y_numeracion()
    {
        await using var cn = await AbrirAsync();

        Assert.True((await LectorOcs.CuentasAsync(cn)).Count > 500);
        _ = await LectorOcs.JobsAsync(cn);
        Assert.Equal("OC", await LectorOcs.UltimoPrefijoAsync(cn));
        Assert.Matches(@"^OC-\d{4,}$", await LectorOcs.ProximaOcAsync(cn, "OC"));
        Assert.Matches(@"^001-001-\d{9}$", await LectorOcs.ProximaRetencionAsync(cn, "001-001", 0));
    }
}
