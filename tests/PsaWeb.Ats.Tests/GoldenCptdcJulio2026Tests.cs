using System.Xml.Serialization;
using PsaWeb.Ats.Esquema;
using PsaWeb.Ats.Validacion;

namespace PsaWeb.Ats.Tests;

/// <summary>
/// Contrasta el esquema regenerado (§3.1/§1.4 de docs/PLAN-APP3-ATS.md) contra
/// el XML <b>real</b> de la declaración ATS de CPTDC de julio/2026 que el
/// usuario dejó en <c>C:\SRI-DIMM\Documentacion\</c> (mismo dato que está en
/// el Sage de CPTDC en PREDATOR — es el golden de F1/F4 del plan). No se
/// commitea al repo por ser información real de un cliente: si el archivo no
/// está en esta máquina, los tests se omiten.
/// </summary>
public class GoldenCptdcJulio2026Tests
{
    private const string RutaGolden = @"C:\SRI-DIMM\Documentacion\ATS CPTDC JULIO  2026.xml";

    private static ivaType CargarGolden()
    {
        using var stream = File.OpenRead(RutaGolden);
        var serializer = new XmlSerializer(typeof(ivaType));
        return (ivaType)serializer.Deserialize(stream)!;
    }

    [SkippableFact]
    public void El_esquema_regenerado_deserializa_el_XML_real_sin_error()
    {
        Skip.IfNot(File.Exists(RutaGolden), "Golden real de CPTDC no está en esta máquina.");

        var ats = CargarGolden();

        Assert.Equal("1792051800001", ats.IdInformante);
        Assert.Equal(
            "CPTDC CHINA PETROLEUM TECHNOLOGY y DEVELOPMENT CORPORATION ECUADOR S A",
            ats.razonSocial);
        Assert.Equal("2026", ats.Anio);
        Assert.Equal("07", ats.Mes);
        Assert.Equal("001", ats.numEstabRuc);
        Assert.Equal(7380817.15m, ats.totalVentas);
        Assert.Equal(codigoOperativoType.IVA, ats.codigoOperativo);
        Assert.Equal(ivaTypeTipoIDInformante.R, ats.TipoIDInformante);
    }

    [SkippableFact]
    public void Trae_los_437_detalles_de_compras_y_6_de_ventas_del_periodo()
    {
        Skip.IfNot(File.Exists(RutaGolden), "Golden real de CPTDC no está en esta máquina.");

        var ats = CargarGolden();

        Assert.Equal(437, ats.compras.Length);
        Assert.Equal(6, ats.ventas.Length);
    }

    [SkippableFact]
    public void ventasEstablecimiento_tiene_1_registro_que_cuadra_con_totalVentas()
    {
        Skip.IfNot(File.Exists(RutaGolden), "Golden real de CPTDC no está en esta máquina.");

        var ats = CargarGolden();

        var establecimiento = Assert.Single(ats.ventasEstablecimiento);
        Assert.Equal("001", establecimiento.codEstab);
        Assert.Equal(ats.totalVentas, establecimiento.ventasEstab);
    }

    [SkippableFact]
    public void anulados_trae_2_facturas_con_autorizacion_de_relleno()
    {
        Skip.IfNot(File.Exists(RutaGolden), "Golden real de CPTDC no está en esta máquina.");

        var ats = CargarGolden();

        Assert.Equal(2, ats.anulados.Length);
        Assert.All(ats.anulados, a =>
        {
            Assert.Equal("18", a.tipoComprobante);
            // Port de LoadCanceled: sin AUT-SRI, cae en "999999" + "9999".
            Assert.Equal("9999999999", a.autorizacion);
        });
    }

    [SkippableFact]
    public void El_campo_2020_valorRetencionNc_se_lee_del_XML_real()
    {
        Skip.IfNot(File.Exists(RutaGolden), "Golden real de CPTDC no está en esta máquina.");

        var ats = CargarGolden();

        // Hallazgo 4 de docs/PLAN-APP3-ATS.md §1.4: el XML real trae este campo
        // (0.00) en cada detalleCompras; el esquema viejo de ATSinScheme no lo
        // tenía. Si esto falla, el esquema volvió a quedar desactualizado.
        Assert.All(ats.compras, c => Assert.True(c.valorRetencionNcSpecified));
    }

    [SkippableFact]
    public void Round_trip_por_EscritorXmlAts_no_pierde_datos()
    {
        Skip.IfNot(File.Exists(RutaGolden), "Golden real de CPTDC no está en esta máquina.");

        var original = CargarGolden();

        var bytes = EscritorXmlAts.Serializar(original);
        using var stream = new MemoryStream(bytes);
        var vueltaAObjeto = (ivaType)new XmlSerializer(typeof(ivaType)).Deserialize(stream)!;

        Assert.Equal(original.IdInformante, vueltaAObjeto.IdInformante);
        Assert.Equal(original.totalVentas, vueltaAObjeto.totalVentas);
        Assert.Equal(original.compras.Length, vueltaAObjeto.compras.Length);
        Assert.Equal(original.ventas.Length, vueltaAObjeto.ventas.Length);
        Assert.Equal(original.anulados.Length, vueltaAObjeto.anulados.Length);
        Assert.Equal(
            original.compras.Sum(c => c.montoIva),
            vueltaAObjeto.compras.Sum(c => c.montoIva));
    }

    [SkippableFact]
    public void ValidadorAts_no_reporta_falsos_bloqueantes_contra_el_XML_real_ya_declarado()
    {
        Skip.IfNot(File.Exists(RutaGolden), "Golden real de CPTDC no está en esta máquina.");

        // El XML real YA declarado es, por definición, un ATS que el SRI aceptó:
        // "Revisar ATS" no debería levantar ningún bloqueante contra él (solo
        // las 2 advertencias fijas sobre límites conocidos de Sage 50).
        var ats = CargarGolden();

        var hallazgos = ValidadorAts.Validar(ats, 2026, 7);
        var bloqueantes = hallazgos.Where(h => h.Severidad == SeveridadHallazgo.Bloqueante).ToList();

        Assert.Empty(bloqueantes);
    }
}
