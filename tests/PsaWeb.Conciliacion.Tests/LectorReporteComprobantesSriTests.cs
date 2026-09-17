using PsaWeb.Conciliacion.Data;

namespace PsaWeb.Conciliacion.Tests;

public class LectorReporteComprobantesSriTests
{
    private const string RucEmpresa = "1799999999001";

    // Fixture sintética del plan (§12.7) — datos 100% inventados, no son de ningún cliente real.
    private const string Encabezado =
        "RUC_EMISOR\tRAZON_SOCIAL_EMISOR\tTIPO_COMPROBANTE\tSERIE_COMPROBANTE\tCLAVE_ACCESO\t" +
        "FECHA_AUTORIZACION\tFECHA_EMISION\tIDENTIFICACION_RECEPTOR\tVALOR_SIN_IMPUESTOS\tIVA\t" +
        "IMPORTE_TOTAL\tNUMERO_DOCUMENTO_MODIFICADO";

    private const string FilaFactura1 =
        "1791111111001\tPROVEEDOR DEMO UNO S.A.\tFactura\t001-001-000000001\t" +
        "0109202601179111111100120010010000000011234567811\t01/09/2026 09:00:00\t01/09/2026\t" +
        RucEmpresa + "\t100\t12\t112\t";

    private const string FilaFactura2 =
        "1791111111001\tPROVEEDOR DEMO UNO S.A.\tFactura\t001-001-000000002\t" +
        "0209202601179111111100120010010000000021234567812\t02/09/2026 10:00:00\t02/09/2026\t" +
        RucEmpresa + "\t50\t6\t56\t";

    private const string FilaNotaCredito =
        "1791222222001\tPROVEEDOR DEMO DOS CÍA. LTDA.\tNota de Crédito\t001-002-000000001\t" +
        "0309202601179122222200120010020000000011234567813\t03/09/2026 11:00:00\t03/09/2026\t" +
        RucEmpresa + "\t10\t1.2\t11.2\t001-001-000000001";

    private const string FilaDecimalSinCeroInicial =
        "1791333333001\tSEÑORÍO Y ASOCIADOS S.A.S.\tFactura\t001-001-000000045\t" +
        "0409202601179133333300120010010000000451234567814\t04/09/2026 08:30:00\t04/09/2026\t" +
        RucEmpresa + "\t1.96\t.29\t2.25\t";

    private static string Reporte(params string[] filas) =>
        string.Join('\n', new[] { Encabezado }.Concat(filas));

    [Fact]
    public void Parsea_todas_las_filas_de_un_reporte_bien_formado()
    {
        var resultado = LectorReporteComprobantesSri.Parsear(
            Reporte(FilaFactura1, FilaFactura2, FilaNotaCredito, FilaDecimalSinCeroInicial), RucEmpresa);

        Assert.Empty(resultado.Errores);
        Assert.Equal(4, resultado.Filas.Count);
    }

    [Fact]
    public void Decimales_sin_cero_inicial_se_parsean_correctamente()
    {
        var resultado = LectorReporteComprobantesSri.Parsear(Reporte(FilaDecimalSinCeroInicial), RucEmpresa);

        var fila = Assert.Single(resultado.Filas);
        Assert.Equal(1.96m, fila.Subtotal);
        Assert.Equal(0.29m, fila.Iva);
        Assert.Equal(2.25m, fila.Total);
    }

    [Fact]
    public void Nota_de_credito_trae_el_numero_de_documento_modificado()
    {
        var resultado = LectorReporteComprobantesSri.Parsear(Reporte(FilaNotaCredito), RucEmpresa);

        var fila = Assert.Single(resultado.Filas);
        Assert.Equal("001-001-000000001", fila.NumeroDocumentoModificado);
    }

    [Fact]
    public void Factura_sin_documento_modificado_queda_null()
    {
        var resultado = LectorReporteComprobantesSri.Parsear(Reporte(FilaFactura1), RucEmpresa);

        Assert.Null(Assert.Single(resultado.Filas).NumeroDocumentoModificado);
    }

    [Fact]
    public void Archivo_solo_con_encabezado_no_es_un_error()
    {
        var resultado = LectorReporteComprobantesSri.Parsear(Encabezado, RucEmpresa);

        Assert.Empty(resultado.Filas);
        Assert.Empty(resultado.Errores);
    }

    [Fact]
    public void Archivo_vacio_lanza_FormatoReporteInvalidoException()
    {
        Assert.Throws<FormatoReporteInvalidoException>(() => LectorReporteComprobantesSri.Parsear("", RucEmpresa));
    }

    [Fact]
    public void Encabezado_alterado_lanza_FormatoReporteInvalidoException()
    {
        var reporteConEncabezadoRoto = Reporte(FilaFactura1).Replace("CLAVE_ACCESO", "CLAVE_DE_ACCESO");

        Assert.Throws<FormatoReporteInvalidoException>(
            () => LectorReporteComprobantesSri.Parsear(reporteConEncabezadoRoto, RucEmpresa));
    }

    [Fact]
    public void Clave_de_48_digitos_va_a_errores_pero_no_frena_el_resto_del_archivo()
    {
        var filaConClaveCorta = FilaFactura1.Replace(
            "0109202601179111111100120010010000000011234567811",
            "010920260117911111110012001001000000001123456781"); // 48 dígitos

        var resultado = LectorReporteComprobantesSri.Parsear(Reporte(filaConClaveCorta, FilaFactura2), RucEmpresa);

        Assert.Single(resultado.Filas);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal(2, error.NumeroFila);
    }

    [Fact]
    public void Clave_con_letras_va_a_errores()
    {
        var filaConLetras = FilaFactura1.Replace(
            "0109202601179111111100120010010000000011234567811",
            "010920260117911111110012001001000000001123456781A");

        var resultado = LectorReporteComprobantesSri.Parsear(Reporte(filaConLetras), RucEmpresa);

        Assert.Empty(resultado.Filas);
        Assert.Single(resultado.Errores);
    }

    [Fact]
    public void Fila_con_receptor_distinto_rechaza_el_archivo_completo()
    {
        var filaOtraEmpresa = FilaFactura1.Replace(RucEmpresa, "1700000000001");

        var ex = Assert.Throws<FormatoReporteInvalidoException>(
            () => LectorReporteComprobantesSri.Parsear(Reporte(FilaFactura1, filaOtraEmpresa), RucEmpresa));
        Assert.Contains("no corresponde a la empresa", ex.Message);
    }

    [Fact]
    public void Misma_clave_de_acceso_repetida_en_el_archivo_no_se_duplica()
    {
        var resultado = LectorReporteComprobantesSri.Parsear(Reporte(FilaFactura1, FilaFactura1), RucEmpresa);

        Assert.Single(resultado.Filas);
        Assert.Empty(resultado.Errores);
    }
}
