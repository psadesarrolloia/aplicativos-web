using PsaWeb.Modules.Reportes.Cheques;
using PsaWeb.Modules.Reportes.Comun;

namespace PsaWeb.Reportes.Tests;

public class ChequesTests
{
    private static readonly ConfiguracionCheque Cfg = new();
    private static readonly OpcionesImpresion Ambos = new();

    private static PagoConDetalle Pago(decimal monto = 35m, string benef = "TONY VERA", string referencia = "3094", int lineas = 2)
    {
        var l = new List<LineaPago>();
        for (var i = 0; i < lineas; i++)
        {
            l.Add(i == 0
                ? new LineaPago(0, "10302-311", "Banco", "TONY VERA", -monto, "", "")
                : new LineaPago(i, $"2002{i}-521", "Cuenta", $"Concepto {i}", i == 1 ? monto : 0m, $"FAC-{i:000}", "PROV"));
        }
        return new PagoConDetalle(
            new PagoCheque(1, new DateOnly(2026, 9, 18), benef, ReferenciaPago.Analizar(referencia)!, monto), l);
    }

    private static PaginaCheque Primera(PagoConDetalle p, ConfiguracionCheque? cfg = null, OpcionesImpresion? o = null,
        string empresa = "EMPRESA DEMO S.A.", string ciudad = "Guayaquil", bool logo = false)
        => ConstructorPaginaCheque.Construir(p, cfg ?? Cfg, empresa, ciudad, logo, o ?? Ambos)[0];

    private static CampoPagina Campo(PaginaCheque p, string id) => p.Campos.Single(c => c.Id == id);

    // --- referencia de pago --------------------------------------------------------------------------

    [Theory]
    [InlineData("3094", "Default", "3094", 3094)]
    [InlineData("PI-1234", "PI-", "1234", 1234)]
    [InlineData("CJ-2796", "CJ-", "2796", 2796)]
    [InlineData("TRF-015-A", "TRF-", "015", 15)]
    [InlineData("TRANS-847", "TRANS-", "847", 847)]
    [InlineData("001-123", "001-", "123", 123)]
    public void ReferenciaPago_separa_tipo_y_numero_como_NumberReference_de_Access(string referencia, string tipo, string numero, long entero)
    {
        var r = ReferenciaPago.Analizar(referencia);
        Assert.NotNull(r);
        Assert.Equal(tipo, r!.Tipo);       // el prefijo INCLUYE el guion
        Assert.Equal(numero, r.Numero);    // se conservan los ceros a la izquierda para imprimir
        Assert.Equal(entero, r.NumeroEntero);
        Assert.Equal(referencia, r.Principal);
    }

    [Theory]
    [InlineData("PI-ABC")]   // Access revienta (Mid con largo negativo): aquí se descarta (Q5)
    [InlineData("PI-")]
    [InlineData("AJ")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("PI-12A")]
    public void ReferenciaPago_descarta_lo_que_no_tiene_numero(string? referencia)
        => Assert.Null(ReferenciaPago.Analizar(referencia));

    // --- filtros ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Filtros_de_la_lista_de_pagos_sobre_la_muestra()
    {
        var repo = new SampleChequesRepository();
        var todo = new FiltroCheques(new(2026, 9, 1), new(2026, 9, 30));

        Assert.Equal(new long[] { 5003, 5002, 5001 }, (await repo.ListarAsync(todo)).Select(p => p.PostOrder).ToArray());
        Assert.Equal(new long[] { 5002 }, (await repo.ListarAsync(todo with { NumeroDesde = 6000, NumeroHasta = 6999 })).Select(p => p.PostOrder).ToArray());
        Assert.Equal(new long[] { 5001 }, (await repo.ListarAsync(todo with { Tipo = "pi-" })).Select(p => p.PostOrder).ToArray());
        Assert.Equal(new long[] { 5003 }, (await repo.ListarAsync(todo with { Beneficiario = "tony" })).Select(p => p.PostOrder).ToArray());
        Assert.Equal(new long[] { 5003 }, (await repo.ListarAsync(todo with { Desde = new(2026, 9, 15) })).Select(p => p.PostOrder).ToArray());
        Assert.True(new FiltroCheques(new(2026, 9, 2), new(2026, 9, 1)).RangoFechasValido is false);
        Assert.False(new FiltroCheques(new(2026, 9, 1), new(2026, 9, 2), NumeroDesde: 9, NumeroHasta: 1).RangoNumerosValido);
    }

    // --- medidas del cheque (las del usuario, en mm desde la esquina de la hoja) --------------------------------

    [Fact]
    public void Las_medidas_del_cheque_son_las_del_reporte_de_Access()
    {
        var p = Primera(Pago(35m));

        var numero = Campo(p, "numero");
        Assert.Equal((170.1, 5.0, 18.9), (numero.X, numero.Y, numero.Ancho));
        Assert.Equal("3094", numero.Texto);

        var benef = Campo(p, "beneficiario");
        Assert.Equal((15.0, 7.0, 80.0), (benef.X, benef.Y, benef.Ancho));
        Assert.Equal("TONY VERA", benef.Texto);

        var monto = Campo(p, "monto");
        Assert.Equal((109.0, 7.0), (monto.X, monto.Y));
        Assert.Equal(Alineacion.Derecha, monto.Alineacion);
        Assert.Equal("35,00", monto.Texto);

        var letras1 = Campo(p, "letras-1");
        Assert.Equal((15.1, 15.0, 100.0), (letras1.X, letras1.Y, letras1.Ancho));

        var rotulo = Campo(p, "rotulo-numero");
        Assert.Equal((170.1, 0.0), (rotulo.X, rotulo.Y));
        Assert.Equal("CH.No.:", rotulo.Texto);

        var ciudadFecha = Campo(p, "ciudad-fecha");
        Assert.Equal((15.1, 26.0), (ciudadFecha.X, ciudadFecha.Y));
        Assert.Equal("Guayaquil, 2026/09/18", ciudadFecha.Texto);

        var r = Assert.Single(p.Rectangulos);
        Assert.Equal((7.9, 77.0, 170.0), (r.X, r.Y, r.Ancho));
    }

    [Fact]
    public void Las_medidas_en_mm_coinciden_con_los_twips_del_mdb()
    {
        // Verificación cruzada: 1 twip = 1/1440″; las cifras del usuario salen de las del reporte de Access.
        Assert.Equal(170.1, Math.Round(Medidas.TwipsAMm(9645), 1));  // n.º de cheque (x)
        Assert.Equal(5.0, Math.Round(Medidas.TwipsAMm(284), 1));     // n.º de cheque (y)
        Assert.Equal(15.0, Math.Round(Medidas.TwipsAMm(851), 1));    // beneficiario (x)
        Assert.Equal(80.0, Math.Round(Medidas.TwipsAMm(4536), 1));   // beneficiario (ancho)
        Assert.Equal(109.0, Math.Round(Medidas.TwipsAMm(6180), 1));  // monto (x)
        Assert.Equal(15.1, Math.Round(Medidas.TwipsAMm(856), 1));    // letras (x)
        Assert.Equal(100.0, Math.Round(Medidas.TwipsAMm(5670), 1));  // letras (ancho)
        Assert.Equal(26.0, Math.Round(Medidas.TwipsAMm(1474), 1));   // ciudad, fecha (y)
        Assert.Equal(7.9, Math.Round(Medidas.TwipsAMm(450), 1));     // recuadro (x)
        Assert.Equal(77.0, Math.Round(Medidas.TwipsAMm(4365), 1));   // recuadro (y)
        Assert.Equal(170.0, Math.Round(Medidas.TwipsAMm(9639), 1));  // recuadro (ancho)
        Assert.Equal(189.0, Math.Round(Medidas.TwipsAMm(10716), 1)); // ancho de página del reporte: 18,9 cm
    }

    [Fact]
    public void El_texto_en_letras_va_en_2_lineas_dentro_de_los_100_mm_aunque_el_relleno_sea_largo()
    {
        var p = Primera(Pago(1234.56m));
        var l1 = Campo(p, "letras-1");
        var l2 = Campo(p, "letras-2");

        Assert.Equal(15.1, l1.X);
        Assert.Equal(15.0, l1.Y);
        Assert.Equal(15.0 + ConstructorPaginaCheque.InterlineaLetras, l2.Y, 3);
        Assert.StartsWith("UN MIL DOSCIENTOS", l1.Texto);
        Assert.DoesNotContain(p.Campos, c => c.Id == "letras-3");

        // Cada línea cabe en los 100 mm con el tamaño con el que se dibujará.
        foreach (var l in new[] { l1, l2 })
        {
            Assert.True(l.Texto.Length <= AjusteTexto.CaracteresPorLinea(100.0, l.Tamano), $"«{l.Texto}» no cabe a {l.Tamano} pt");
            Assert.InRange(l.Tamano, AjusteTexto.TamanoMinimo, 10);
        }
    }

    [Fact]
    public void Monto_cero_o_anulado_imprime_CERO_DOLARES()
    {
        var p = Primera(Pago(0m, "ANULADO", "6615", lineas: 0));
        Assert.Equal("CERO DOLARES", Campo(p, "letras-1").Texto);
        Assert.Equal("0,00", Campo(p, "monto").Texto);
    }

    [Fact]
    public void Bug_Q1_se_conserva_y_se_corrige_con_la_opcion()
    {
        var p150 = Pago(1.5m);
        Assert.True(p150.Pago.PierdeCentavosPorBugQ1);
        Assert.Equal("UN", Campo(Primera(p150), "letras-1").Texto);

        var corregido = Primera(p150, new ConfiguracionCheque { CorregirMontosMenoresA2 = true });
        Assert.StartsWith("UN CON 50/100", Campo(corregido, "letras-1").Texto);
    }

    // --- configuración: nada fijo a una empresa ------------------------------------------------------------------

    [Fact]
    public void Ciudad_empresa_y_firmas_son_configurables_y_no_hay_Quito_ni_Efemedio_fijos()
    {
        var cfg = new ConfiguracionCheque { Firma1 = "RECIBI CONFORME", Firma2 = "ELABORADO POR", Firma3 = "REVISADO POR", RotuloNumero = "CHEQUE No." };
        var p = Primera(Pago(), cfg, empresa: "EMISORA ACME CIA. LTDA.", ciudad: "Cuenca");

        Assert.Equal("EMISORA ACME CIA. LTDA.", Campo(p, "empresa").Texto);
        Assert.True(Campo(p, "empresa").Negrita);
        Assert.Equal("COMPROBANTE DE EGRESO", Campo(p, "titulo").Texto);
        Assert.Equal("Cuenca, 2026/09/18", Campo(p, "ciudad-fecha").Texto);
        Assert.Equal(new[] { "RECIBI CONFORME", "ELABORADO POR", "REVISADO POR" },
            new[] { "firma1", "firma2", "firma3" }.Select(id => Campo(p, id).Texto).ToArray());
        Assert.Equal("CHEQUE No.", Campo(p, "rotulo-numero").Texto);

        Assert.DoesNotContain(p.Campos, c => c.Texto.Contains("Quito", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(p.Campos, c => c.Texto.Contains("EFEMEDIO", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(p.Campos, c => c.Texto.Contains("DEMORADIO", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sin_ciudad_el_texto_es_solo_la_fecha_y_el_rotulo_se_puede_ocultar()
    {
        var p = Primera(Pago(), new ConfiguracionCheque { ImprimirRotuloNumero = false }, ciudad: "");
        Assert.Equal("2026/09/18", Campo(p, "ciudad-fecha").Texto);
        Assert.DoesNotContain(p.Campos, c => c.Id == "rotulo-numero");
    }

    [Theory]
    [InlineData("es", "1.234,56")]
    [InlineData("en", "1,234.56")]
    public void Formato_del_monto(string formato, string esperado)
        => Assert.Equal(esperado, ConstructorPaginaCheque.FormatearMonto(1234.56m, formato));

    [Fact]
    public void Se_puede_imprimir_solo_el_cheque_o_solo_el_comprobante()
    {
        var soloCheque = Primera(Pago(), o: new OpcionesImpresion(Comprobante: false));
        Assert.Contains(soloCheque.Campos, c => c.Id == "numero");
        Assert.DoesNotContain(soloCheque.Campos, c => c.Id == "empresa");
        Assert.Empty(soloCheque.Rectangulos);

        var soloComprobante = Primera(Pago(), o: new OpcionesImpresion(Cheque: false));
        Assert.DoesNotContain(soloComprobante.Campos, c => c.Id == "numero");
        Assert.Contains(soloComprobante.Campos, c => c.Id == "empresa");
        Assert.Single(soloComprobante.Rectangulos);
    }

    // --- comprobante de egreso ------------------------------------------------------------------------------

    [Fact]
    public void El_comprobante_coloca_encabezado_tabla_filas_y_totales_en_las_medidas_del_reporte()
    {
        var p = Primera(Pago(35m));

        Assert.Equal((9.0, 98.4), (Campo(p, "nombre-etiqueta").X, Campo(p, "nombre-etiqueta").Y));
        Assert.Equal((33.0, 98.4), (Campo(p, "nombre").X, Campo(p, "nombre").Y));
        Assert.Equal((117.9, 98.5), (Campo(p, "fecha-etiqueta").X, Campo(p, "fecha-etiqueta").Y));
        Assert.Equal("18/09/2026", Campo(p, "fecha").Texto);
        Assert.Equal((9.0, 106.4), (Campo(p, "concepto-etiqueta").X, Campo(p, "concepto-etiqueta").Y));

        Assert.Contains(p.Lineas, l => l.Y1 == 112.5 && l.X1 == 7.9 && l.X2 == 177.9);
        Assert.Equal(114.6, Campo(p, "th-codigo").Y);
        Assert.Equal(new[] { 8.5, 24.6, 58.7, 139.7, 159.5 },
            new[] { "th-codigo", "th-factura", "th-descripcion", "th-pagos", "th-cheque" }.Select(id => Campo(p, id).X).ToArray());

        // Fila 1: el banco (Amount < 0) va en CHEQUE; fila 2: la cuenta pagada (Amount > 0) va en PAGOS.
        Assert.Equal("0,00", Campo(p, "fila1-pagos").Texto);
        Assert.Equal("35,00", Campo(p, "fila1-cheque").Texto);
        Assert.Equal("35,00", Campo(p, "fila2-pagos").Texto);
        Assert.Equal("0,00", Campo(p, "fila2-cheque").Texto);
        Assert.Equal(120.5 + 0.5, Campo(p, "fila1-codigo").Y, 3);
        Assert.Equal(120.5 + 6.47 + 0.5, Campo(p, "fila2-codigo").Y, 3);

        // Totales = MainAmount en PAGOS y CHEQUE, bajo la última fila.
        var yFin = 120.5 + 2 * 6.47;
        Assert.Equal(yFin + 2.9, Campo(p, "total-pagos").Y, 3);
        Assert.Equal("35,00", Campo(p, "total-pagos").Texto);
        Assert.Equal("35,00", Campo(p, "total-cheque").Texto);
        Assert.Equal((139.7, 159.4), (Campo(p, "total-pagos").X, Campo(p, "total-cheque").X));
    }

    [Fact]
    public void Las_firmas_tienen_su_linea_de_40_mm_en_el_pie()
    {
        var p = Primera(Pago());
        Assert.Equal(new[] { 23.0, 80.0, 136.0 }, new[] { "firma1", "firma2", "firma3" }.Select(id => Campo(p, id).X).ToArray());
        foreach (var x in new[] { 23.0, 80.0, 136.0 })
        {
            Assert.Contains(p.Lineas, l => l.X1 == x && l.X2 == x + 40.0 && l.Y1 == 253.7);
        }
    }

    [Fact]
    public void Un_pago_con_muchas_lineas_sigue_en_hojas_de_continuacion_con_los_totales_solo_al_final()
    {
        var pago = Pago(lineas: 40);
        var hojas = ConstructorPaginaCheque.Construir(pago, Cfg, "EMPRESA", "Quito", false, Ambos);

        Assert.Equal(3, hojas.Count);  // 18 + 18 + 4
        Assert.Contains(hojas[0].Campos, c => c.Id == "numero");            // el cheque sólo en la 1.ª
        Assert.DoesNotContain(hojas[1].Campos, c => c.Id == "numero");
        Assert.All(hojas, h => Assert.Contains(h.Campos, c => c.Id == "empresa"));
        Assert.DoesNotContain(hojas[0].Campos, c => c.Id == "total-pagos");
        Assert.DoesNotContain(hojas[1].Campos, c => c.Id == "total-pagos");
        Assert.Contains(hojas[2].Campos, c => c.Id == "total-pagos");
        Assert.Contains(hojas[1].Campos, c => c.Id == "titulo" && c.Texto.Contains("continuación"));
        Assert.Equal(18, ConstructorPaginaCheque.FilasPorHoja);
        Assert.All(hojas, h => Assert.Equal(3, h.Campos.Count(c => c.Id.StartsWith("firma"))));
    }

    [Fact]
    public void Textos_largos_se_achican_para_no_salirse_de_su_caja()
    {
        var p = Primera(Pago(benef: "PROVEEDOR CON UN NOMBRE MUY LARGO PARA PROBAR EL AJUSTE S.A."));
        var b = Campo(p, "beneficiario");
        Assert.True(b.Tamano < 10);
        Assert.True(b.Texto.Length <= AjusteTexto.CaracteresPorLinea(80.0, b.Tamano));

        var normal = Campo(Primera(Pago()), "beneficiario");
        Assert.Equal(10, normal.Tamano); // los nombres cortos no se tocan
    }

    // --- calibración X/Y --------------------------------------------------------------------------------------------

    [Fact]
    public void La_correccion_XY_desplaza_todos_los_campos_lineas_y_recuadros()
    {
        var base0 = Primera(Pago(), logo: true);
        var corr = Primera(Pago(), new ConfiguracionCheque { CorreccionX = 3.0, CorreccionY = -2.5 }, logo: true);

        Assert.Equal(base0.Campos.Count, corr.Campos.Count);
        for (var i = 0; i < base0.Campos.Count; i++)
        {
            Assert.Equal(base0.Campos[i].X + 3.0, corr.Campos[i].X, 6);
            Assert.Equal(base0.Campos[i].Y - 2.5, corr.Campos[i].Y, 6);
            Assert.Equal(base0.Campos[i].Ancho, corr.Campos[i].Ancho); // el ancho no cambia
        }
        Assert.Equal(base0.Lineas[0].X1 + 3.0, corr.Lineas[0].X1, 6);
        Assert.Equal(base0.Rectangulos[0].Y - 2.5, corr.Rectangulos[0].Y, 6);
        Assert.Equal(base0.Logo!.X + 3.0, corr.Logo!.X, 6);
    }

    // --- ajuste de texto ---------------------------------------------------------------------------------------------

    [Fact]
    public void AjusteTexto_en_courier_10_caben_47_caracteres_en_100_mm()
    {
        Assert.Equal(47, AjusteTexto.CaracteresPorLinea(100.0, 10));
        Assert.Equal(37, AjusteTexto.CaracteresPorLinea(80.0, 10));
        Assert.Equal(10, AjusteTexto.TamanoParaUnaLinea("HOLA", 80.0, 10));
    }

    [Fact]
    public void PartirEnLineas_corta_en_espacios_y_achica_hasta_que_quepa()
    {
        var texto = string.Join(" ", Enumerable.Repeat("PALABRA", 14)); // 111 caracteres
        var (lineas, tam) = AjusteTexto.PartirEnLineas(texto, 100.0, 10, 2);

        Assert.Equal(2, lineas.Count);
        Assert.True(tam < 10);
        Assert.All(lineas, l => Assert.True(l.Length <= AjusteTexto.CaracteresPorLinea(100.0, tam)));
        Assert.Equal(texto, string.Join(" ", lineas));
    }

    [Fact]
    public void PartirEnLineas_corta_en_seco_una_palabra_mas_larga_que_la_linea()
    {
        var (lineas, _) = AjusteTexto.PartirEnLineas(new string('X', 60), 50.0, 10, 3);
        Assert.Equal(new string('X', 23), lineas[0]); // 50 mm / 2,1167 mm = 23 caracteres
        Assert.Equal(3, lineas.Count);
    }
}
