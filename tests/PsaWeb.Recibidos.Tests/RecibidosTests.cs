using System.Security;
using Microsoft.EntityFrameworkCore;
using PsaWeb.Recibidos.Data;

namespace PsaWeb.Recibidos.Tests;

internal static class Datos
{
    internal const string RucEmpresa = "1799999998001";    // comprador del XML sintético
    internal const string Clave = "0101202601179999999900120010020000001231234567811";
    internal static readonly string Factura = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "factura-sintetica.xml"));

    /// <summary>La misma factura dentro de la respuesta SOAP del WS de autorización.</summary>
    internal static string Soap(string comprobante = "", string numero = "1") => $"""
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body>
        <ns2:autorizacionComprobanteResponse xmlns:ns2="http://ec.gob.sri.ws.autorizacion"><RespuestaAutorizacionComprobante>
        <claveAccesoConsultada>{Clave}</claveAccesoConsultada><numeroComprobantes>{numero}</numeroComprobantes><autorizaciones>
        {(numero == "0" ? "" : $"<autorizacion><estado>AUTORIZADO</estado><fechaAutorizacion>2026-01-01T08:28:54-05:00</fechaAutorizacion><comprobante>{SecurityElement.Escape(comprobante.Length > 0 ? comprobante : Factura)}</comprobante></autorizacion>")}
        </autorizaciones></RespuestaAutorizacionComprobante></ns2:autorizacionComprobanteResponse></soap:Body></soap:Envelope>
        """;
}

public class DescargadorXmlSriTests
{
    [Fact]
    public void Interpreta_la_respuesta_con_y_sin_comprobante()
    {
        var con = DescargadorXmlSri.Interpretar(Datos.Soap());
        Assert.NotNull(con.Contenido);

        var sin = DescargadorXmlSri.Interpretar(Datos.Soap(numero: "0"));
        Assert.Null(sin.Contenido);
        Assert.True(sin.SinComprobante);

        Assert.NotNull(DescargadorXmlSri.Interpretar("no es xml").Error);
    }

    [Fact]
    public void Clave_de_acceso_da_fecha_y_tipo()
    {
        Assert.Equal(new DateOnly(2026, 1, 1), ServicioDescargaXml.FechaEmision(Datos.Clave));
        Assert.Equal("01", ServicioDescargaXml.CodigoTipo(Datos.Clave));
        Assert.Null(ServicioDescargaXml.FechaEmision("123"));
    }
}

[Collection("PlataformaRecibidos")]
public class AlmacenXmlRecibidosTests : IAsyncLifetime
{
    private const string Local = @"Server=.\SQLEXPRESS;Database=PsaWebPlataforma;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15";

    internal sealed class Fabrica : IDbContextFactory<RecibidosDbContext>
    {
        public RecibidosDbContext CreateDbContext() => new(new DbContextOptionsBuilder<RecibidosDbContext>().UseSqlServer(Local).Options);
    }

    private static bool? _disponible;
    internal static bool Disponible()
    {
        if (_disponible is not null) return _disponible.Value;
        try
        {
            using var db = new Fabrica().CreateDbContext();
            if (!db.Database.CanConnect()) return (_disponible = false).Value;
            db.Database.Migrate();
            return (_disponible = true).Value;
        }
        catch { return (_disponible = false).Value; }
    }

    public Task InitializeAsync() => Limpiar();
    public Task DisposeAsync() => Limpiar();

    internal static async Task Limpiar()
    {
        if (!Disponible()) return;
        await using var db = new Fabrica().CreateDbContext();
        await db.Xmls.Where(x => x.Ruc == Datos.RucEmpresa).ExecuteDeleteAsync();
        await db.Conflictos.Where(x => x.Ruc == Datos.RucEmpresa).ExecuteDeleteAsync();
        await db.DescargasFallidas.Where(x => x.Ruc == Datos.RucEmpresa).ExecuteDeleteAsync();
    }

    [SkippableFact]
    public async Task Guarda_una_vez_comprimido_y_lo_devuelve_igual()
    {
        Skip.IfNot(Disponible(), "PsaWebPlataforma local no disponible.");
        var almacen = new AlmacenXmlRecibidos(new Fabrica());

        var g = await almacen.GuardarAsync(Datos.RucEmpresa, Datos.Soap(), OrigenesXml.WsSri, "tester", Datos.Clave);
        Assert.Equal(ResultadoGuardadoXml.Guardado, g.Resultado);
        Assert.Equal(Datos.Soap(), await almacen.ObtenerAsync(Datos.RucEmpresa, Datos.Clave));
        Assert.Contains(Datos.Clave, await almacen.ConXmlAsync(Datos.RucEmpresa, [Datos.Clave, "x"]));

        // El mismo comprobante suelto (otro envoltorio) no es conflicto.
        Assert.Equal(ResultadoGuardadoXml.YaExistia, (await almacen.GuardarAsync(Datos.RucEmpresa, Datos.Factura, OrigenesXml.SubidaManual, "tester")).Resultado);

        var periodo = await almacen.DelPeriodoAsync(Datos.RucEmpresa, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), "Factura");
        var fila = Assert.Single(periodo);
        Assert.Equal(("1799999999001", OrigenesXml.WsSri), (fila.RucEmisor, fila.Origen));
        Assert.NotNull(fila.FechaAutorizacion);
    }

    [SkippableFact]
    public async Task Otro_xml_para_la_misma_clave_queda_como_conflicto_y_no_pisa()
    {
        Skip.IfNot(Disponible(), "PsaWebPlataforma local no disponible.");
        var almacen = new AlmacenXmlRecibidos(new Fabrica());
        await almacen.GuardarAsync(Datos.RucEmpresa, Datos.Factura, OrigenesXml.SubidaManual, "tester");

        var distinto = Datos.Factura.Replace("<importeTotal>130.50</importeTotal>", "<importeTotal>999.00</importeTotal>");
        var g = await almacen.GuardarAsync(Datos.RucEmpresa, distinto, OrigenesXml.SubidaManual, "tester");

        Assert.Equal(ResultadoGuardadoXml.Conflicto, g.Resultado);
        Assert.Equal(Datos.Factura, await almacen.ObtenerAsync(Datos.RucEmpresa, Datos.Clave));
    }

    [SkippableFact]
    public async Task Rechaza_otro_receptor_otra_clave_y_lo_que_no_es_comprobante()
    {
        Skip.IfNot(Disponible(), "PsaWebPlataforma local no disponible.");
        var almacen = new AlmacenXmlRecibidos(new Fabrica());

        var otro = await almacen.GuardarAsync("1799999997001", Datos.Factura, OrigenesXml.SubidaManual, "tester");
        Assert.Equal((ResultadoGuardadoXml.Rechazado, "1799999998001"), (otro.Resultado, otro.Receptor));

        Assert.Equal(ResultadoGuardadoXml.Rechazado,
            (await almacen.GuardarAsync(Datos.RucEmpresa, Datos.Factura, OrigenesXml.SubidaManual, "tester", claveEsperada: new string('1', 49))).Resultado);
        Assert.Equal(ResultadoGuardadoXml.Rechazado, (await almacen.GuardarAsync(Datos.RucEmpresa, "<x/>", OrigenesXml.SubidaManual, "tester")).Resultado);
    }

    [SkippableFact]
    public async Task Otro_receptor_se_acepta_con_confirmacion()
    {
        Skip.IfNot(Disponible(), "PsaWebPlataforma local no disponible.");
        var almacen = new AlmacenXmlRecibidos(new Fabrica());
        await using var db = new Fabrica().CreateDbContext();
        await db.Xmls.Where(x => x.Ruc == "1799999997001").ExecuteDeleteAsync();

        var g = await almacen.GuardarAsync("1799999997001", Datos.Factura, OrigenesXml.SubidaManual, "tester", aceptarOtroReceptor: true);

        Assert.Equal(ResultadoGuardadoXml.Guardado, g.Resultado);
        await db.Xmls.Where(x => x.Ruc == "1799999997001").ExecuteDeleteAsync();
    }
}

[Collection("PlataformaRecibidos")]
public class ServicioDescargaXmlTests : IAsyncLifetime
{
    private sealed class DescargadorFalso(Func<string, ResultadoDescargaXml> respuesta) : IDescargadorXmlSri
    {
        public int Llamadas;
        public Task<ResultadoDescargaXml> DescargarAsync(string claveAcceso, CancellationToken cancellationToken = default)
        {
            Llamadas++;
            return Task.FromResult(respuesta(claveAcceso));
        }
    }

    public Task InitializeAsync() => AlmacenXmlRecibidosTests.Limpiar();
    public Task DisposeAsync() => AlmacenXmlRecibidosTests.Limpiar();

    [SkippableFact]
    public async Task Descarga_guarda_y_registra_lo_que_el_ws_no_entrega()
    {
        Skip.IfNot(AlmacenXmlRecibidosTests.Disponible(), "PsaWebPlataforma local no disponible.");
        ServicioDescargaXml.Pausa = TimeSpan.Zero;
        var fabrica = new AlmacenXmlRecibidosTests.Fabrica();
        var otraClave = "0101202601179999999900120010020000009991234567815";
        var falso = new DescargadorFalso(c => c == Datos.Clave ? new(Datos.Soap(), false, null) : new(null, true, null));
        var servicio = new ServicioDescargaXml(falso, new AlmacenXmlRecibidos(fabrica), fabrica);

        var r = await servicio.DescargarAsync(Datos.RucEmpresa, [Datos.Clave, otraClave, "no-es-clave"], "tester");

        Assert.Equal((2, 1, 0, 1, 0), (r.Pedidas, r.Descargadas, r.YaEstaban, r.FueraDeVentana, r.Errores));
        var fallidas = await servicio.FallidasAsync(Datos.RucEmpresa);
        Assert.Equal(ServicioDescargaXml.MotivoSinXml, fallidas[otraClave].Motivo);

        // Segunda pasada: la que ya tiene XML no se pide; la vieja fuera de ventana tampoco se vuelve a pedir.
        falso.Llamadas = 0;
        var r2 = await servicio.DescargarAsync(Datos.RucEmpresa, [Datos.Clave, otraClave], "tester");
        Assert.Equal((1, 1, 0), (r2.YaEstaban, r2.FueraDeVentana, falso.Llamadas));
    }
}
