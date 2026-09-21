using System.Globalization;
using PsaWeb.Modules.Reportes.Comun;

namespace PsaWeb.Modules.Reportes.Cheques;

/// <summary>
/// Arma la(s) hoja(s) A4 de un pago: el cheque en la esquina superior izquierda y, debajo, el comprobante de egreso.
/// Las medidas son las del reporte <c>PaymentsProof</c> de Access (twips → mm, verificadas contra las que dio el
/// usuario) y se guardan como constantes en milímetros desde la esquina superior izquierda de la hoja. A todo se le
/// suma la corrección X/Y de <see cref="ConfiguracionCheque"/> (calibración de impresión, ver D3 del plan).
/// </summary>
public static class ConstructorPaginaCheque
{
    // --- cheque (mm; las del usuario) -------------------------------------------------------------------
    internal static readonly (double X, double Y, double Ancho) RotuloNumero = (170.1, 0.0, 13.8);
    internal static readonly (double X, double Y, double Ancho) Numero = (170.1, 5.0, 18.9);
    internal static readonly (double X, double Y, double Ancho) Beneficiario = (15.0, 7.0, 80.0);
    internal static readonly (double X, double Y, double Ancho) Monto = (109.0, 7.0, 25.0);
    internal static readonly (double X, double Y, double Ancho) Letras = (15.1, 15.0, 100.0);
    internal static readonly (double X, double Y, double Ancho) CiudadFecha = (15.1, 26.0, 84.0);
    internal const int LineasLetras = 2;
    internal const double InterlineaLetras = 4.6;

    // --- comprobante de egreso (twips del .mdb → mm) --------------------------------------------------------
    internal static readonly RectanguloPagina Recuadro = new(7.9, 77.0, 170.0, 17.0, 0.35);
    internal static readonly ImagenPagina PosicionLogo = new(49.9, 77.5, 16.3, 16.6);
    internal const double TituloX = 70.0, EmpresaY = 78.5, TituloY = 86.7, TituloAncho = 105.0;
    internal const double NombreEtiquetaX = 9.0, NombreY = 98.4, NombreX = 33.0, NombreAncho = 77.9;
    internal const double FechaEtiquetaX = 117.9, FechaY = 98.5, FechaX = 137.9, FechaAncho = 25.0;
    internal const double ConceptoY = 106.4;
    internal const double LineaTablaY = 112.5, EncabezadoTablaY = 114.6;
    internal const double TablaX1 = 7.9, TablaX2 = 177.9;
    internal const double FilasInicioY = 120.5, FilaAlto = 6.47, FilaTextoDy = 0.5;
    internal const double PieLineaDy = 2.1, PieTotalDy = 2.9;
    // Pie de hoja anclado al margen INFERIOR: 253,7 mm desde el borde superior de la hoja = 240,7 desde el margen superior de 13,0
    // (así, con el desplazamiento por defecto, cae en 253,7 y no se corre con el margen superior).
    internal const double FirmaY = 240.7, FirmaAncho = 40.0;
    internal static readonly double[] FirmaX = { 23.0, 80.0, 136.0 };

    // Columnas de la tabla (X, ancho).
    internal static readonly (double X, double Ancho) ColCodigo = (8.5, 15.0);
    internal static readonly (double X, double Ancho) ColFactura = (24.6, 33.1);
    internal static readonly (double X, double Ancho) ColDescripcion = (58.7, 79.9);
    internal static readonly (double X, double Ancho) ColPagos = (139.7, 18.8);
    internal static readonly (double X, double Ancho) ColCheque = (159.5, 18.8);

    /// <summary>Filas de detalle por hoja: hasta el pie (firmas) dejando lugar a los totales.</summary>
    internal static readonly int FilasPorHoja = (int)Math.Floor((FirmaY - 10.7 - FilasInicioY) / FilaAlto);

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-EC");
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    /// <summary>Monto con 2 decimales: «1.234,56» (es, como «Standard» de Access en Windows es-EC) o «1,234.56» (en).</summary>
    public static string FormatearMonto(decimal monto, string formato)
        => monto.ToString("N2", string.Equals(formato, "en", StringComparison.OrdinalIgnoreCase) ? En : Es);

    public static IReadOnlyList<PaginaCheque> Construir(
        PagoConDetalle pago,
        ConfiguracionCheque cfg,
        string nombreEmpresa,
        string ciudad,
        bool hayLogo,
        OpcionesImpresion opciones,
        IMedidorTexto? medidor = null)
    {
        medidor ??= MedidorTexto.Para(cfg.Fuente);
        var paginas = new List<PaginaCheque>();
        var total = pago.Lineas.Count;
        var hojas = Math.Max(1, (int)Math.Ceiling(total / (double)FilasPorHoja));

        for (var h = 0; h < hojas; h++)
        {
            var campos = new List<CampoPagina>();
            var lineas = new List<LineaPagina>();
            var rectangulos = new List<RectanguloPagina>();
            ImagenPagina? logo = null;

            if (opciones.Cheque && h == 0)
            {
                AgregarCheque(campos, pago.Pago, cfg, ciudad, medidor);
            }

            if (opciones.Comprobante)
            {
                var filas = pago.Lineas.Skip(h * FilasPorHoja).Take(FilasPorHoja).ToList();
                var ultima = h == hojas - 1;
                logo = AgregarComprobante(campos, lineas, rectangulos, pago.Pago, filas, cfg, nombreEmpresa, hayLogo, ultima, h > 0, medidor);
            }

            paginas.Add(Desplazar(new PaginaCheque(campos, lineas, rectangulos, logo), cfg.CorreccionX, cfg.CorreccionY));
        }

        return paginas;
    }

    /// <summary>Suma la corrección de calibración X/Y a todas las coordenadas de la hoja.</summary>
    internal static PaginaCheque Desplazar(PaginaCheque p, double dx, double dy)
    {
        if (dx == 0 && dy == 0)
        {
            return p;
        }
        return new PaginaCheque(
            p.Campos.Select(c => c with { X = c.X + dx, Y = c.Y + dy }).ToList(),
            p.Lineas.Select(l => l with { X1 = l.X1 + dx, X2 = l.X2 + dx, Y1 = l.Y1 + dy, Y2 = l.Y2 + dy }).ToList(),
            p.Rectangulos.Select(r => r with { X = r.X + dx, Y = r.Y + dy }).ToList(),
            p.Logo is { } l2 ? l2 with { X = l2.X + dx, Y = l2.Y + dy } : null);
    }

    // ------------------------------------------------------------------------------------------------

    private static void AgregarCheque(List<CampoPagina> campos, PagoCheque pago, ConfiguracionCheque cfg, string ciudad, IMedidorTexto medidor)
    {
        var t = cfg.Tamano;

        if (cfg.ImprimirRotuloNumero && cfg.RotuloNumero.Length > 0)
        {
            campos.Add(Campo("rotulo-numero", RotuloNumero, cfg.RotuloNumero, t, medidor));
        }
        campos.Add(Campo("numero", Numero, pago.Referencia.Numero, t, medidor));
        campos.Add(Campo("beneficiario", Beneficiario, pago.Beneficiario, t, medidor));
        campos.Add(Campo("monto", Monto, FormatearMonto(pago.Monto, cfg.FormatoMonto), t, medidor, Alineacion.Derecha));

        var letras = NumeroALetras.ValorLetras(pago.Monto, cfg.CorregirMontosMenoresA2);
        var (lineas, tam) = AjusteTexto.PartirEnLineas(letras, Letras.Ancho, t, LineasLetras, medidor);
        for (var i = 0; i < lineas.Count; i++)
        {
            campos.Add(new CampoPagina($"letras-{i + 1}", Letras.X, Letras.Y + i * InterlineaLetras, Letras.Ancho, lineas[i],
                Alineacion.Izquierda, tam));
        }

        var fechaTexto = pago.Fecha.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        var ciudadFecha = string.IsNullOrWhiteSpace(ciudad) ? fechaTexto : $"{ciudad.Trim()}, {fechaTexto}";
        campos.Add(Campo("ciudad-fecha", CiudadFecha, ciudadFecha, t, medidor));
    }

    private static ImagenPagina? AgregarComprobante(
        List<CampoPagina> campos, List<LineaPagina> lineas, List<RectanguloPagina> rectangulos,
        PagoCheque pago, IReadOnlyList<LineaPago> filas, ConfiguracionCheque cfg,
        string nombreEmpresa, bool hayLogo, bool ultimaHoja, bool continuacion, IMedidorTexto medidor)
    {
        var t = cfg.Tamano;
        rectangulos.Add(Recuadro);

        campos.Add(new CampoPagina("empresa", TituloX, EmpresaY, TituloAncho, nombreEmpresa,
            Alineacion.Izquierda, AjusteTexto.TamanoParaUnaLinea(nombreEmpresa, TituloAncho, t, medidor), Negrita: true));
        var titulo = continuacion ? "COMPROBANTE DE EGRESO (continuación)" : "COMPROBANTE DE EGRESO";
        campos.Add(new CampoPagina("titulo", TituloX + 0.1, TituloY, TituloAncho, titulo,
            Alineacion.Izquierda, AjusteTexto.TamanoParaUnaLinea(titulo, TituloAncho, t, medidor)));

        campos.Add(new CampoPagina("nombre-etiqueta", NombreEtiquetaX, NombreY, 21.0, "NOMBRE:", Alineacion.Izquierda, t));
        campos.Add(new CampoPagina("nombre", NombreX, NombreY, NombreAncho, pago.Beneficiario,
            Alineacion.Izquierda, AjusteTexto.TamanoParaUnaLinea(pago.Beneficiario, NombreAncho, t, medidor)));
        campos.Add(new CampoPagina("fecha-etiqueta", FechaEtiquetaX, FechaY, 13.0, "FECHA:", Alineacion.Izquierda, t));
        campos.Add(new CampoPagina("fecha", FechaX, FechaY, FechaAncho,
            pago.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), Alineacion.Izquierda, t));
        campos.Add(new CampoPagina("concepto-etiqueta", NombreEtiquetaX, ConceptoY, 20.4, "CONCEPTO:", Alineacion.Izquierda, t));

        lineas.Add(new LineaPagina(TablaX1, LineaTablaY, TablaX2, LineaTablaY, 0.35));
        campos.Add(new CampoPagina("th-codigo", ColCodigo.X, EncabezadoTablaY, ColCodigo.Ancho, "CODIGO", Alineacion.Izquierda, t));
        campos.Add(new CampoPagina("th-factura", ColFactura.X, EncabezadoTablaY, ColFactura.Ancho, "REFER./FACTURA", Alineacion.Izquierda,
            AjusteTexto.TamanoParaUnaLinea("REFER./FACTURA", ColFactura.Ancho, t, medidor)));
        campos.Add(new CampoPagina("th-descripcion", ColDescripcion.X, EncabezadoTablaY, ColDescripcion.Ancho, "DESCRIPCION", Alineacion.Izquierda, t));
        campos.Add(new CampoPagina("th-pagos", ColPagos.X, EncabezadoTablaY, ColPagos.Ancho, cfg.EncabezadoDebito, Alineacion.Izquierda, t));
        campos.Add(new CampoPagina("th-cheque", ColCheque.X, EncabezadoTablaY, ColCheque.Ancho, cfg.EncabezadoCredito, Alineacion.Derecha, t));

        for (var i = 0; i < filas.Count; i++)
        {
            var l = filas[i];
            var y = FilasInicioY + i * FilaAlto + FilaTextoDy;
            campos.Add(Celda($"fila{i + 1}-codigo", ColCodigo, y, l.CuentaId, Alineacion.Izquierda, t, medidor));
            campos.Add(Celda($"fila{i + 1}-factura", ColFactura, y, l.Factura, Alineacion.Izquierda, t, medidor));
            campos.Add(Celda($"fila{i + 1}-descripcion", ColDescripcion, y, l.Descripcion, Alineacion.Izquierda, t, medidor));
            campos.Add(Celda($"fila{i + 1}-pagos", ColPagos, y, FormatearMonto(l.Pagos, cfg.FormatoMonto), Alineacion.Derecha, t, medidor));
            campos.Add(Celda($"fila{i + 1}-cheque", ColCheque, y, FormatearMonto(l.Cheque, cfg.FormatoMonto), Alineacion.Derecha, t, medidor));
        }

        if (ultimaHoja)
        {
            var yFin = FilasInicioY + filas.Count * FilaAlto;
            lineas.Add(new LineaPagina(TablaX1, yFin + PieLineaDy, TablaX2, yFin + PieLineaDy, 0.35));
            var total = FormatearMonto(pago.Monto, cfg.FormatoMonto);
            campos.Add(Celda("total-pagos", (139.7, 18.7), yFin + PieTotalDy, total, Alineacion.Derecha, t, medidor));
            campos.Add(Celda("total-cheque", (159.4, 18.7), yFin + PieTotalDy, total, Alineacion.Derecha, t, medidor));
        }

        // Pie de hoja (en cada hoja): 3 firmas con su línea.
        var rotulos = new[] { cfg.Firma1, cfg.Firma2, cfg.Firma3 };
        for (var i = 0; i < 3; i++)
        {
            lineas.Add(new LineaPagina(FirmaX[i], FirmaY, FirmaX[i] + FirmaAncho, FirmaY, 0.25));
            if (rotulos[i].Length > 0)
            {
                campos.Add(new CampoPagina($"firma{i + 1}", FirmaX[i], FirmaY + 0.3, FirmaAncho, rotulos[i], Alineacion.Centro,
                    AjusteTexto.TamanoParaUnaLinea(rotulos[i], FirmaAncho, t, medidor)));
            }
        }

        return hayLogo ? PosicionLogo : null;
    }

    private static CampoPagina Campo(string id, (double X, double Y, double Ancho) pos, string texto, double tamano,
        IMedidorTexto medidor, Alineacion alineacion = Alineacion.Izquierda)
        => new(id, pos.X, pos.Y, pos.Ancho, texto, alineacion, AjusteTexto.TamanoParaUnaLinea(texto, pos.Ancho, tamano, medidor));

    private static CampoPagina Celda(string id, (double X, double Ancho) col, double y, string texto, Alineacion a, double tamano, IMedidorTexto medidor)
        => new(id, col.X, y, col.Ancho, texto, a, AjusteTexto.TamanoParaUnaLinea(texto, col.Ancho, tamano, medidor));
}
