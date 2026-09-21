using System.Globalization;
using PsaWeb.Modules.Reportes.Comun;

namespace PsaWeb.Modules.Reportes.Cheques;

/// <summary>
/// Hoja de prueba de impresión (calibración). Sirve para decidir dos cosas con una sola hoja impresa:
/// <list type="number">
///   <item>que la escala fue del 100 % (una línea de 100,0 mm) y dónde cae el (0, 0) real de la impresora;</item>
///   <item>si las medidas de Access se leen desde la <b>esquina de la hoja</b> o desde el <b>margen del reporte</b>
///   (10,0 mm izquierdo / 13,0 mm superior, decodificados del <c>.mdb</c>; ver D3): se marca ese punto y se imprime un
///   cheque de ejemplo con cada campo enmarcado, en las coordenadas actuales incluida la corrección X/Y.</item>
/// </list>
/// Se pega el cheque en la esquina superior izquierda, se imprime, se compara y se carga la corrección X/Y.
/// </summary>
public static class HojaPruebaCheque
{
    public const double MargenAccessX = 10.0;
    public const double MargenAccessY = 13.0;

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-EC");

    public static PaginaCheque Construir(ConfiguracionCheque cfg, string nombreEmpresa, string ciudad)
    {
        var campos = new List<CampoPagina>();
        var lineas = new List<LineaPagina>();
        var rectangulos = new List<RectanguloPagina>();

        // --- reglas en mm, sin la corrección: son referencias de la hoja ------------------------------------
        // Borde superior: sólo marcas (sin números: ahí queda pegado el cheque). Regla rotulada bajo el cheque.
        const double pequeno = 5.0;
        for (var x = 0; x <= 200; x += 10)
        {
            lineas.Add(new LineaPagina(x, 0, x, x % 50 == 0 ? 3.0 : 1.5, 0.2));
        }
        const double reglaY = 62.0;
        lineas.Add(new LineaPagina(0, reglaY, 200, reglaY, 0.2));
        for (var x = 0; x <= 200; x += 10)
        {
            lineas.Add(new LineaPagina(x, reglaY - (x % 50 == 0 ? 3.0 : 1.8), x, reglaY, 0.2));
            if (x % 20 == 0)
            {
                campos.Add(new CampoPagina($"regla-x{x}", x - 4.0, reglaY + 0.8, 8.0, x.ToString(CultureInfo.InvariantCulture), Alineacion.Centro, pequeno));
            }
        }
        for (var y = 10; y <= 290; y += 10)
        {
            var largo = y % 50 == 0 ? 3.0 : 1.5;
            lineas.Add(new LineaPagina(0, y, largo, y, 0.2));
            if (y % 20 == 0)
            {
                campos.Add(new CampoPagina($"regla-y{y}", 3.4, y - 1.2, 8.0, y.ToString(CultureInfo.InvariantCulture), Alineacion.Izquierda, pequeno));
            }
        }

        // Esquina (0, 0) de la hoja y punto del margen del reporte de Access.
        Cruz(lineas, 0.0, 0.0, 4.0);
        Cruz(lineas, MargenAccessX, MargenAccessY, 4.0);
        campos.Add(new CampoPagina("marca-margen", MargenAccessX + 4.5, MargenAccessY - 1.0, 110.0,
            $"(+{MargenAccessX:0.0}; +{MargenAccessY:0.0}) = margen izq./sup. del reporte de Access", Alineacion.Izquierda, 5.5));

        // --- regla de 100 mm para comprobar la escala ---------------------------------------------------------
        lineas.Add(new LineaPagina(10, 46, 110, 46, 0.5));
        lineas.Add(new LineaPagina(10, 44.5, 10, 47.5, 0.5));
        lineas.Add(new LineaPagina(110, 44.5, 110, 47.5, 0.5));
        campos.Add(new CampoPagina("regla-100", 10, 48, 150.0,
            "Esta línea debe medir 100,0 mm. Si mide otra cosa, la impresión NO salió al 100 % (\"tamaño real\").",
            Alineacion.Izquierda, 6.0));

        // --- cheque de ejemplo con las coordenadas actuales (incluye la corrección X/Y) -----------------------
        var ejemplo = new PagoConDetalle(
            new PagoCheque(0, new DateOnly(2026, 9, 21), "BENEFICIARIO DE EJEMPLO S.A.",
                new ReferenciaPago("1234", ReferenciaPago.TipoPorDefecto, "1234", 1234), 1234.56m),
            Array.Empty<LineaPago>());
        var pagina = ConstructorPaginaCheque.Construir(ejemplo, cfg, nombreEmpresa, ciudad, hayLogo: false,
            new OpcionesImpresion(Cheque: true, Comprobante: false))[0];
        campos.AddRange(pagina.Campos);

        // Cada campo enmarcado (alto = tamaño de fuente en mm × 1,3).
        foreach (var c in pagina.Campos)
        {
            var alto = Math.Max(3.0, Medidas.PuntosAMm(c.Tamano) * 1.3);
            rectangulos.Add(new RectanguloPagina(c.X, c.Y, c.Ancho, alto, 0.1));
        }

        // --- leyenda con las coordenadas (sin corrección) y la corrección vigente ----------------------------------
        var ly = 100.0;
        campos.Add(new CampoPagina("leyenda-titulo", 15, ly, 180, "HOJA DE PRUEBA DE IMPRESION — cheque en la esquina superior izquierda", Alineacion.Izquierda, 8, true));
        var leyenda = new (string Nombre, (double X, double Y, double Ancho) Pos)[]
        {
            ("N.º de cheque", ConstructorPaginaCheque.Numero),
            ("Beneficiario", ConstructorPaginaCheque.Beneficiario),
            ("Monto", ConstructorPaginaCheque.Monto),
            ("Monto en letras (2 líneas)", ConstructorPaginaCheque.Letras),
            ("Ciudad, fecha", ConstructorPaginaCheque.CiudadFecha),
        };
        var i = 0;
        foreach (var (nombre, pos) in leyenda)
        {
            campos.Add(new CampoPagina($"leyenda-{i}", 15, ly + 6 + i * 4.5, 180.0,
                $"{nombre}: x = {pos.X.ToString("0.0", Es)} mm · y = {pos.Y.ToString("0.0", Es)} mm · ancho = {pos.Ancho.ToString("0.0", Es)} mm",
                Alineacion.Izquierda, 7));
            i++;
        }
        campos.Add(new CampoPagina("leyenda-correccion", 15, ly + 6 + i * 4.5 + 2, 180.0,
            $"Corrección vigente: X = {cfg.CorreccionX.ToString("0.0", Es)} mm · Y = {cfg.CorreccionY.ToString("0.0", Es)} mm (ya sumada arriba).",
            Alineacion.Izquierda, 7, true));
        campos.Add(new CampoPagina("leyenda-pasos", 15, ly + 6 + i * 4.5 + 8, 185.0,
            "1) Pegue el cheque con cinta en la esquina superior izquierda.  2) Imprima al 100 %.", Alineacion.Izquierda, 6.5));
        campos.Add(new CampoPagina("leyenda-pasos-2", 15, ly + 6 + i * 4.5 + 12, 185.0,
            "3) Mida cuánto se corrió cada campo.  4) Cargue la corrección X/Y (mm) en la página.", Alineacion.Izquierda, 6.5));

        return new PaginaCheque(campos, lineas, rectangulos);
    }

    private static void Cruz(List<LineaPagina> lineas, double x, double y, double medio)
    {
        lineas.Add(new LineaPagina(x - medio, y, x + medio, y, 0.3));
        lineas.Add(new LineaPagina(x, y - medio, x, y + medio, 0.3));
    }
}
