using System.Data.Odbc;
using Microsoft.EntityFrameworkCore;
using PsaWeb.Modules.Compras.Servicios;
using PsaWeb.PeachEbills.Data;
using PsaWeb.Recibidos;
using PsaWeb.Recibidos.Data;
using PsaWeb.Sage50;
using PsaWeb.SageBridge.Cola;
using PsaWeb.SageBridge.Cola.Data;

namespace PsaWeb.Compras.Tests.Validacion;

/// <summary>
/// F5 — bandeja de recibidos y formulario desde el XML, contra la copia de prueba y el almacén local con los XML reales de la F2
/// (cargados por <c>CargaXmlRealesTests</c>). SOLO LECTURA. Variables en <see cref="EntornoCompras"/>.
/// </summary>
public class RecibidosCopiaTests
{
    private sealed class Conexiones(string cadena) : ISageConnectionFactory
    {
        public OdbcConnection CreateConnection() => new(cadena);
        public OdbcConnection CreateConnection(string connectionString) => new(connectionString);
    }

    private sealed class Fabrica<T>(Func<T> crear) : IDbContextFactory<T> where T : DbContext
    {
        public T CreateDbContext() => crear();
    }

    private sealed class Servicios(Dictionary<Type, object> mapa) : IServiceProvider
    {
        public object? GetService(Type t) => mapa.GetValueOrDefault(t);
    }

    private static (ServicioRecibidos Recibidos, ServicioCompras Compras) Crear()
    {
        var peach = new Fabrica<PeachEbillsContext>(EntornoCompras.PeachEbills);
        var plataforma = new Fabrica<RecibidosDbContext>(() => new RecibidosDbContext(
            new DbContextOptionsBuilder<RecibidosDbContext>().UseSqlServer(EntornoCompras.PlataformaLocal).Options));
        var bridge = new Fabrica<SageBridgeDbContext>(() => new SageBridgeDbContext(
            new DbContextOptionsBuilder<SageBridgeDbContext>().UseSqlServer(EntornoCompras.PlataformaLocal).Options));
        var almacen = new AlmacenXmlRecibidos(plataforma);
        var sp = new Servicios(new Dictionary<Type, object> { [typeof(IAlmacenXmlRecibidos)] = almacen });
        var compras = new ServicioCompras(new Conexiones(EntornoCompras.CadenaSage!), new SinShellResolverEmpresaSage(), peach,
            new ColaSage(bridge), new AuditoriaSage(bridge), sp);
        return (new ServicioRecibidos(compras, sp, peach), compras);
    }

    [SkippableFact]
    public async Task Bandeja_y_formulario_desde_el_xml_real()
    {
        Skip.If(EntornoCompras.CadenaSage is null, "Sin PSAWEB_TEST_SAGE_COMPRAS.");
        Skip.If(Environment.Is64BitProcess, "El driver ODBC de Pervasive es de 32 bits.");
        var (recibidos, compras) = Crear();

        var bandeja = await recibidos.BandejaAsync(EntornoCompras.Ruc, new DateOnly(2026, 9, 11), new DateOnly(2026, 9, 23), ServicioRecibidos.TipoFactura);
        Skip.If(bandeja.Count == 0, "El almacén local no tiene los XML reales (correr CargaXmlRealesTests con PSAWEB_TEST_CARGAR_XML=1).");

        // Todas las facturas de la F2 están registradas en la copia (OC convertida en compra por el worker).
        Assert.All(bandeja, d => Assert.Equal(EstadoRecibido.Registrado, d.Estado));
        Assert.All(bandeja, d => Assert.Matches(@"^\d{3}-\d{3}-\d{9}$", d.Serie));

        var d0 = bandeja.First(d => d.ReferenciaOc == "OC-8757");
        var carga = await recibidos.CargarFacturaAsync(EntornoCompras.Ruc, d0.ClaveAcceso, "tester");

        Assert.Null(carga.Error);
        Assert.False(carga.FaltaXml);
        var f = carga.Formulario!;
        Assert.Equal(("001-008-001262988", ModoFormulario.Existente, "OC-8757", "001-001-000025829"),
            (f.NumeroFactura, f.Modo, f.NumeroOcExistente, f.NumeroRetencionExistente));
        Assert.True(carga.OcExistente!.Recibida);
        Assert.Contains(carga.Avisos, a => a.Contains("ya está registrada"));
        Assert.Equal("PETROPLATINUM CIA. L", f.Proveedor!.Existente!.Id);
        Assert.Equal(64, carga.HuellaXml!.Length);

        // El formulario precargado arma la misma OC que registró el `.exe` (tarjeta de crédito → C3 + 332G).
        var armado = PsaWeb.Compras.Armado.ArmadorOc.Armar(f.Entrada(EntornoCompras.Ruc, (await compras.ContextoAsync(EntornoCompras.Ruc)).Catalogo)!,
            (await compras.ContextoAsync(EntornoCompras.Ruc)).Catalogo);
        Assert.True(armado.Correcto, string.Join(" / ", armado.Errores));
        Assert.Equal(["C3", "15% IVA COMPRAS", "AUT-SRI", "0% 332G - Sin Ret.TC", null, null], armado.Oc!.Lineas.Select(l => l.ItemId));
    }
}
