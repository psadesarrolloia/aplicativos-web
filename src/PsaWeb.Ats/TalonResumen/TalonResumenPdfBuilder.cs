using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PsaWeb.Ats.TalonResumen;

/// <summary>
/// Genera el Talón Resumen en PDF a partir de <see cref="InformacionTalonAts"/>,
/// reproduciendo el layout del Talón real del DIMM (§3.5b del plan) con
/// QuestPDF (licencia Community). No reusa el logo/HTML del SRI — solo el
/// contenido y el orden de las tablas, verificado campo por campo contra
/// <c>TRSMN-ATS-07-2026-CPTDC.pdf</c>.
/// </summary>
public static class TalonResumenPdfBuilder
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    static TalonResumenPdfBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Generar(InformacionTalonAts info)
    {
        var documento = Document.Create(contenedor =>
        {
            contenedor.Page(pagina =>
            {
                pagina.Size(PageSizes.A4);
                pagina.Margin(30);
                pagina.DefaultTextStyle(x => x.FontSize(8));

                pagina.Header().Element(c => Cabecera(c, info));
                pagina.Content().Element(c => Cuerpo(c, info));
                pagina.Footer().AlignCenter().Text(t =>
                {
                    t.Span("TALÓN RESUMEN ATS — Página ");
                    t.CurrentPageNumber();
                    t.Span(" de ");
                    t.TotalPages();
                });
            });
        });

        return documento.GeneratePdf();
    }

    private static void Cabecera(IContainer contenedor, InformacionTalonAts info)
    {
        contenedor.Column(col =>
        {
            col.Item().AlignCenter().Text("TALÓN RESUMEN").Bold().FontSize(12);
            col.Item().AlignCenter().Text("SERVICIO DE RENTAS INTERNAS").Bold().FontSize(10);
            col.Item().AlignCenter().Text("ANEXO TRANSACCIONAL").Bold().FontSize(10);
            col.Item().PaddingTop(4).AlignCenter().Text(info.RazonSocial).FontSize(9);
            col.Item().AlignCenter().Text($"RUC: {info.Ruc}").FontSize(9);
            col.Item().AlignCenter().Text($"Periodo: {info.Periodo}").FontSize(9);
            col.Item().AlignCenter().Text($"Fecha de Generación: {info.FechaGeneracion:dd/MM/yyyy HH:mm:ss}").FontSize(9);
            col.Item().PaddingTop(8).Text(
                $"Certifico que la información contenida en el medio magnético del Anexo Transaccional para el " +
                $"período {info.Periodo}, es fiel reflejo del siguiente reporte:");
        });
    }

    private static void Cuerpo(IContainer contenedor, InformacionTalonAts info)
    {
        contenedor.PaddingTop(6).Column(col =>
        {
            col.Spacing(10);
            col.Item().Element(c => TablaTipoComprobante(c, "COMPRAS", info.Compras, info.TotalCompras));
            col.Item().Element(c => TablaTipoComprobante(c, "VENTAS", info.Ventas, info.TotalVentas));
            col.Item().Element(c => TablaAnulados(c, info.ComprobantesAnulados));
            col.Item().Text("RESUMEN DE RETENCIONES - AGENTE DE RETENCION").Bold();
            col.Item().Element(c => TablaRetencionRenta(c, info));
            col.Item().Element(c => TablaRetencionIva(c, info));
            col.Item().Element(c => TablaRetencionesRecibidas(c, info));
            col.Item().PaddingTop(10).Text(
                "Declaro que los datos contenidos en este anexo son verdaderos, por lo que asumo la " +
                "responsabilidad correspondiente, de acuerdo a lo establecido en el Art. 101 de la Codificación " +
                "de la Ley de Régimen Tributario Interno");
            col.Item().PaddingTop(30).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().BorderBottom(1).PaddingBottom(2).Text(" ");
                    c.Item().AlignCenter().Text("Firma del Contador").Bold();
                });
                row.ConstantItem(20);
                row.RelativeItem().Column(c =>
                {
                    c.Item().BorderBottom(1).PaddingBottom(2).Text(" ");
                    c.Item().AlignCenter().Text("Firma del Representante Legal").Bold();
                });
            });
        });
    }

    private static void TablaTipoComprobante(
        IContainer contenedor, string titulo, IReadOnlyList<FilaTipoComprobante> filas, FilaTipoComprobante total)
    {
        contenedor.Column(col =>
        {
            col.Item().Background(Colors.Grey.Lighten2).Padding(2).Text(titulo).Bold();
            col.Item().Table(tabla =>
            {
                tabla.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(30);
                    c.RelativeColumn(3);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                });

                tabla.Header(header =>
                {
                    Encabezado(header, "Cod.");
                    Encabezado(header, "Transacción");
                    Encabezado(header, "No. Registros");
                    Encabezado(header, "BI tarifa 0%");
                    Encabezado(header, "BI tarifa diferente 0%");
                    Encabezado(header, "BI No Objeto IVA");
                    Encabezado(header, "Valor IVA");
                });

                foreach (var f in filas)
                {
                    Celda(tabla, f.Codigo);
                    Celda(tabla, f.Transaccion);
                    CeldaNum(tabla, f.NumRegistros.ToString(Culture));
                    CeldaNum(tabla, Num(f.BiTarifa0));
                    CeldaNum(tabla, Num(f.BiTarifaDiferente0));
                    CeldaNum(tabla, Num(f.BiNoObjetoIva));
                    CeldaNum(tabla, Num(f.ValorIva));
                }

                CeldaTotal(tabla, "TOTAL:", 3);
                CeldaNumTotal(tabla, Num(total.BiTarifa0));
                CeldaNumTotal(tabla, Num(total.BiTarifaDiferente0));
                CeldaNumTotal(tabla, Num(total.BiNoObjetoIva));
                CeldaNumTotal(tabla, Num(total.ValorIva));
            });
        });
    }

    private static void TablaAnulados(IContainer contenedor, int comprobantesAnulados)
    {
        contenedor.Column(col =>
        {
            col.Item().Background(Colors.Grey.Lighten2).Padding(2).Text("COMPROBANTES ANULADOS").Bold();
            col.Item().Row(row =>
            {
                row.RelativeItem(5).Border(1).Padding(3)
                    .Text("Total de Comprobantes Anulados en el período informado (no incluye los dados de baja)");
                row.RelativeItem(1).Border(1).Padding(3).AlignRight().Text(comprobantesAnulados.ToString(Culture));
            });
        });
    }

    private static void TablaRetencionRenta(IContainer contenedor, InformacionTalonAts info)
    {
        contenedor.Column(col =>
        {
            col.Item().Text("RETENCION EN LA FUENTE DE IMPUESTO A LA RENTA").Italic();
            col.Item().Table(tabla =>
            {
                tabla.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(35);
                    c.RelativeColumn(4);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                });

                tabla.Header(header =>
                {
                    Encabezado(header, "Cod.");
                    Encabezado(header, "Concepto de Retención");
                    Encabezado(header, "No. Registros");
                    Encabezado(header, "Base Imponible");
                    Encabezado(header, "Valor Retenido");
                });

                foreach (var f in info.RetencionesRenta)
                {
                    Celda(tabla, f.Codigo);
                    Celda(tabla, f.Concepto);
                    CeldaNum(tabla, f.NumRegistros.ToString(Culture));
                    CeldaNum(tabla, Num(f.BaseImponible));
                    CeldaNum(tabla, Num(f.ValorRetenido));
                }

                CeldaTotal(tabla, "TOTAL:", 3);
                CeldaNumTotal(tabla, Num(info.TotalRetencionesRenta.BaseImponible));
                CeldaNumTotal(tabla, Num(info.TotalRetencionesRenta.ValorRetenido));
            });
        });
    }

    private static void TablaRetencionIva(IContainer contenedor, InformacionTalonAts info)
    {
        contenedor.Column(col =>
        {
            col.Item().Text("RETENCION EN LA FUENTE DE IVA").Italic();
            col.Item().Table(tabla =>
            {
                tabla.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1);
                    c.RelativeColumn(2);
                    c.RelativeColumn(1);
                });

                tabla.Header(header =>
                {
                    Encabezado(header, "Operación");
                    Encabezado(header, "Concepto de Retención");
                    Encabezado(header, "Valor Retenido");
                });

                foreach (var f in info.RetencionesIva)
                {
                    Celda(tabla, "COMPRA");
                    Celda(tabla, f.Concepto);
                    CeldaNum(tabla, Num(f.ValorRetenido));
                }

                CeldaTotal(tabla, "TOTAL:", 2);
                CeldaNumTotal(tabla, Num(info.TotalRetencionesIva));
            });
        });
    }

    private static void TablaRetencionesRecibidas(IContainer contenedor, InformacionTalonAts info)
    {
        contenedor.Column(col =>
        {
            col.Item().Background(Colors.Grey.Lighten2).Padding(2)
                .Text("RESUMEN DE RETENCIONES QUE LE EFECTUARON EN EL PERIODO").Bold();
            col.Item().Table(tabla =>
            {
                tabla.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1);
                    c.RelativeColumn(2);
                    c.RelativeColumn(1);
                });

                tabla.Header(header =>
                {
                    Encabezado(header, "Operación");
                    Encabezado(header, "Concepto de Retención");
                    Encabezado(header, "Valor Retenido");
                });

                Celda(tabla, "VENTA");
                Celda(tabla, "Valor de IVA que le han retenido");
                CeldaNum(tabla, Num(info.RetencionesRecibidasIva));

                Celda(tabla, "VENTA");
                Celda(tabla, "Valor de Renta que le han retenido");
                CeldaNum(tabla, Num(info.RetencionesRecibidasRenta));

                CeldaTotal(tabla, "TOTAL:", 2);
                CeldaNumTotal(tabla, Num(info.TotalRetencionesRecibidas));
            });
        });
    }

    private static void Encabezado(TableCellDescriptor header, string texto) =>
        header.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(3).Text(texto).Bold();

    private static void Celda(TableDescriptor tabla, string texto) =>
        tabla.Cell().Border(1).Padding(2).Text(texto);

    private static void CeldaNum(TableDescriptor tabla, string texto) =>
        tabla.Cell().Border(1).Padding(2).AlignRight().Text(texto);

    private static void CeldaTotal(TableDescriptor tabla, string texto, int colspan) =>
        tabla.Cell().ColumnSpan((uint)colspan).Border(1).Padding(2).AlignRight().Text(texto).Bold();

    private static void CeldaNumTotal(TableDescriptor tabla, string texto) =>
        tabla.Cell().Border(1).Padding(2).AlignRight().Text(texto).Bold();

    private static string Num(decimal valor) => valor.ToString("N2", Culture);
}
