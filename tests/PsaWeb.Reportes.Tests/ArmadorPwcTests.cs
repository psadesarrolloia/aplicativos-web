using PsaWeb.Modules.Reportes.Pwc;

namespace PsaWeb.Reportes.Tests;

public class ArmadorPwcTests
{
    private static CabeceraFacturaPwc Cab(long po = 1, decimal? total = 100m, decimal? pagado = 0m, string ciudad = "GYE")
        => new(po, "CLIENTE", $"001-001-{po:000000000}", new(2026, 9, 1), new(2026, 10, 1), total, pagado,
            "ANUNCIANTE", "DIR", "ORD-1", ciudad);

    private static IReadOnlyList<FilaPwc> Armar(
        CabeceraFacturaPwc cab, IEnumerable<GrupoImpuestoCrudo>? grupos = null, IEnumerable<RetencionCruda>? ret = null)
        => ArmadorPwc.Armar(new[] { cab }, grupos ?? Array.Empty<GrupoImpuestoCrudo>(), ret ?? Array.Empty<RetencionCruda>());

    [Fact]
    public void Subtotal_es_RowType_0_e_IVA_es_el_resto_en_valor_absoluto()
    {
        var f = Armar(Cab(), new[]
        {
            new GrupoImpuestoCrudo(1, 0, "", -1000m),
            new GrupoImpuestoCrudo(1, 5, "IVA", -120m),
        }).Single();

        Assert.Equal(1000m, f.Subtotal);
        Assert.Equal(120m, f.Iva);
    }

    [Fact]
    public void Los_grupos_de_monto_cero_se_ignoran()
    {
        var f = Armar(Cab(), new[]
        {
            new GrupoImpuestoCrudo(1, 0, "", -1000m),
            new GrupoImpuestoCrudo(1, 1, "", 0m),
            new GrupoImpuestoCrudo(1, 2, "", 0m),
            new GrupoImpuestoCrudo(1, 5, "IVA", -120m),
        }).Single();

        Assert.Equal(1000m, f.Subtotal);
        Assert.Equal(120m, f.Iva);
    }

    [Fact]
    public void Bug_P3_el_ultimo_grupo_no_cero_gana_no_se_suman()
    {
        var f = Armar(Cab(), new[]
        {
            new GrupoImpuestoCrudo(1, 0, "A", -700m),
            new GrupoImpuestoCrudo(1, 0, "B", -300m),
        }).Single();

        Assert.Equal(300m, f.Subtotal); // Access sobrescribe la celda con cada grupo
    }

    [Fact]
    public void Sin_grupos_subtotal_e_IVA_son_cero()
    {
        var f = Armar(Cab()).Single();
        Assert.Equal(0m, f.Subtotal);
        Assert.Equal(0m, f.Iva);
    }

    [Fact]
    public void Retenciones_IRF_e_IVA_con_su_porcentaje_leido_de_la_descripcion()
    {
        var f = Armar(Cab(total: 1120m, pagado: 94m), ret: new[]
        {
            new RetencionCruda(1, "IRF", -10m, "1% RET IMP RENTA"),
            new RetencionCruda(1, "IVA", -84m, "IVA 70%"),
        }).Single();

        Assert.Equal(10m, f.RetRenta);
        Assert.Equal(84m, f.RetIva);
        Assert.Equal(1m, f.RetencionRentaGanadora!.Porcentaje);
        Assert.Equal(70m, f.RetencionIvaGanadora!.Porcentaje);
    }

    [Fact]
    public void Bug_P6_con_dos_retenciones_del_mismo_tipo_gana_la_ultima()
    {
        var f = Armar(Cab(), ret: new[]
        {
            new RetencionCruda(1, "IRF", 10m, "1% A"),
            new RetencionCruda(1, "IRF", 30m, "3% B"),
        }).Single();

        Assert.Equal(30m, f.RetRenta);
        Assert.Equal(3m, f.RetencionRentaGanadora!.Porcentaje);
    }

    [Fact]
    public void Un_JobID_desconocido_no_cuenta_como_retencion()
    {
        var f = Armar(Cab(), ret: new[] { new RetencionCruda(1, "OTRO", 50m, "5%") }).Single();
        Assert.Equal(0m, f.RetRenta);
        Assert.Equal(0m, f.RetIva);
    }

    [Theory]
    [InlineData(1120, 94, 1026)]
    [InlineData(500, 0, 500)]
    public void Monto_a_cobrar_es_total_menos_pagado(decimal total, decimal pagado, decimal esperado)
        => Assert.Equal(esperado, Armar(Cab(total: total, pagado: pagado)).Single().MontoCobrar);

    [Fact]
    public void Pagado_nulo_deja_el_total_y_total_nulo_deja_ceros()
    {
        Assert.Equal(250m, Armar(Cab(total: 250m, pagado: null)).Single().MontoCobrar);

        var sinTotal = Armar(Cab(total: null, pagado: 50m)).Single();
        Assert.Equal(0m, sinTotal.Total);
        Assert.Equal(0m, sinTotal.MontoCobrar);
    }

    [Fact]
    public void Descuentos_es_total_menos_retenciones_menos_monto_a_cobrar_como_la_formula_de_la_columna_N()
    {
        // Caso real de Efemedio (001-001-000010646): K=896, L=8, M=67,2, O=301,2 → N=519,6
        var f = Armar(Cab(total: 896m, pagado: 594.8m), ret: new[]
        {
            new RetencionCruda(1, "IRF", 8m, "1% RET IMP RENTA"),
            new RetencionCruda(1, "IVA", 67.2m, "IVA 70%"),
        }).Single();

        Assert.Equal(301.2m, f.MontoCobrar);
        Assert.Equal(519.6m, f.Descuentos);
    }

    [Theory]
    [InlineData("309 - RF 3%", 3)]
    [InlineData("3440 - RF 3%", 3)]
    [InlineData("1.75% RET IMP RENTA", 1.75)]
    [InlineData("1,75% RET IMP RENTA", 1.75)]
    [InlineData("IVA 70%", 70)]
    [InlineData("70% RET IVA", 70)]
    [InlineData("2 % algo", 2)]
    public void PorcentajeRetencion_extrae_el_primer_porcentaje(string descripcion, double esperado)
        => Assert.Equal((decimal)esperado, PorcentajeRetencion.Extraer(descripcion));

    [Theory]
    [InlineData("RETENCION IVA")]
    [InlineData("RETENCION FUENTE")]
    [InlineData("")]
    [InlineData(null)]
    public void PorcentajeRetencion_devuelve_null_si_no_hay_porcentaje(string? descripcion)
        => Assert.Null(PorcentajeRetencion.Extraer(descripcion));

    // --- resúmenes (sobre los datos de muestra) -----------------------------------------------------

    private static async Task<ResultadoPwc> Muestra(FiltroPwc? filtro = null)
        => await new SamplePwcRepository().GenerarAsync(filtro ?? new FiltroPwc());

    [Fact]
    public async Task Totales_de_la_muestra()
    {
        var r = await Muestra();

        Assert.Equal(4, r.Filas.Count);
        Assert.Equal(5600m, r.TotalFacturado);
        Assert.Equal(96.25m, r.TotalRetRenta);
        Assert.Equal(432m, r.TotalRetIva);
        Assert.Equal(4571.75m, r.TotalMontoCobrar);
        Assert.Equal(500m, r.TotalDescuentos);
    }

    [Fact]
    public async Task El_resumen_de_retenciones_es_dinamico_ordenado_y_cuadra_con_L_mas_M()
    {
        var r = await Muestra();
        var resumen = r.ResumenRetenciones();

        Assert.Equal(
            new[] { "RET. IR 1%", "RET. IR 1,75%", "RET. IR 3%", "RET. IVA 70%", "RET. IVA (sin %)" },
            resumen.Select(x => x.Etiqueta).ToArray());
        Assert.Equal(new[] { 10m, 26.25m, 60m, 252m, 180m }, resumen.Select(x => x.Total).ToArray());
        Assert.Equal(r.TotalRetRenta + r.TotalRetIva, resumen.Sum(x => x.Total));
    }

    [Fact]
    public async Task El_resumen_por_ciudad_deja_sin_ciudad_al_final()
    {
        var resumen = (await Muestra()).ResumenPorCiudad();

        Assert.Equal(new[] { "GYE", "UIO", "(sin ciudad)" }, resumen.Select(c => c.Etiqueta).ToArray());
        Assert.Equal(new[] { 2499.75m, 1512m, 560m }, resumen.Select(c => c.MontoCobrar).ToArray());
        Assert.Equal(new[] { 2, 1, 1 }, resumen.Select(c => c.Facturas).ToArray());
    }

    // --- filtros ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Filtro_por_emision_es_inclusivo()
    {
        var r = await Muestra(new FiltroPwc(EmisionDesde: new(2026, 8, 10), EmisionHasta: new(2026, 8, 20)));
        Assert.Equal(new[] { "001-001-000000102", "001-001-000000103" }, r.Filas.Select(f => f.Factura).ToArray());
    }

    [Fact]
    public async Task Filtro_por_cobranza_excluye_facturas_sin_fecha_de_vencimiento_o_fuera_de_rango()
    {
        var r = await Muestra(new FiltroPwc(VenceDesde: new(2026, 9, 20)));
        Assert.Equal(new[] { "001-001-000000104" }, r.Filas.Select(f => f.Factura).ToArray());
    }

    [Fact]
    public async Task Filtro_de_texto_ignora_mayusculas_y_tildes()
    {
        var r = await Muestra(new FiltroPwc(Cliente: "publicidad demo dos"));
        Assert.Equal(2, r.Filas.Count);

        var sinTilde = await Muestra(new FiltroPwc(Anunciante: "cliente final b"));
        Assert.Single(sinTilde.Filas);
    }

    [Fact]
    public async Task Filtro_por_ciudad_incluye_sin_ciudad_con_la_cadena_vacia()
    {
        var soloSin = await Muestra(new FiltroPwc(Ciudades: new[] { FiltroPwc.SinCiudad }));
        Assert.Equal(new[] { "001-001-000000103" }, soloSin.Filas.Select(f => f.Factura).ToArray());

        var gyeYUio = await Muestra(new FiltroPwc(Ciudades: new[] { "gye", "UIO" }));
        Assert.Equal(3, gyeYUio.Filas.Count);
    }

    [Fact]
    public void Rangos_invertidos_son_invalidos()
    {
        Assert.False(new FiltroPwc(EmisionDesde: new(2026, 9, 2), EmisionHasta: new(2026, 9, 1)).RangoEmisionValido);
        Assert.False(new FiltroPwc(VenceDesde: new(2026, 9, 2), VenceHasta: new(2026, 9, 1)).RangoVenceValido);
        Assert.True(new FiltroPwc().RangoEmisionValido);
    }

    [Fact]
    public void Describir_lista_los_filtros_aplicados()
    {
        Assert.Equal("Sin filtros", new FiltroPwc().Describir());
        var d = new FiltroPwc(EmisionDesde: new(2026, 9, 1), Ciudades: new[] { "GYE", "" }, Cliente: "abc").Describir();
        Assert.Contains("Emisión 01/09/2026–…", d);
        Assert.Contains("Ciudad: GYE, (sin ciudad)", d);
        Assert.Contains("Cliente contiene «abc»", d);
    }

    [Fact]
    public async Task Opciones_listan_ciudades_y_clientes_presentes()
    {
        var o = await new SamplePwcRepository().OpcionesAsync();
        Assert.Equal(new[] { "GYE", "UIO", "" }, o.Ciudades.ToArray());
        Assert.Equal(2, o.Clientes.Count);
    }
}
