using System.Globalization;
using System.Text.RegularExpressions;
using PsaWeb.Ats.Esquema;

namespace PsaWeb.Ats.Validacion;

/// <summary>Gravedad de un hallazgo: bloqueante (el XML no debería subirse así) o advertencia (revisar antes de declarar).</summary>
public enum SeveridadHallazgo
{
    Advertencia,
    Bloqueante,
}

/// <summary>Un hallazgo de "Revisar ATS". <see cref="Codigo"/> es estable, para que la UI/tests lo referencien sin parsear el texto.</summary>
public sealed record HallazgoAts(SeveridadHallazgo Severidad, string Codigo, string Mensaje);

/// <summary>
/// "Revisar ATS" (§3.5a del plan): corre sobre el <c>ivaType</c> ya armado, antes
/// de habilitar la descarga del XML. No reemplaza al validador oficial del DIMM
/// (<c>rig-ats-validacion</c>) — reproduce sus reglas más relevantes (estructurales
/// del XSD, cruce ventas↔establecimientos, compras, duplicados) más 2 advertencias
/// fijas sobre límites reales de lo que Sage 50 puede registrar (confirmados
/// contra el golden real de CPTDC julio/2026 en F2/F3 — ver docs/PLAN-APP3-ATS.md).
/// Parte pura, sin ODBC/EF.
/// </summary>
public static class ValidadorAts
{
    private const decimal Tolerancia = 0.02m;
    private static readonly Regex RazonSocialValida = new(@"^[A-Za-z0-9\s]*$", RegexOptions.Compiled);
    private static readonly DateOnly FechaMinimaSri = new(2002, 1, 1);

    public static IReadOnlyList<HallazgoAts> Validar(ivaType ats, int anio, int mes)
    {
        var hallazgos = new List<HallazgoAts>();

        ValidarEstructurales(ats, hallazgos);
        ValidarCruceVentasEstablecimientos(ats, hallazgos);
        ValidarCompras(ats, anio, mes, hallazgos);
        ValidarDuplicadosCompras(ats, hallazgos);

        // No se valida "forma de pago" por umbral/fecha (decisión 2026-09-12,
        // docs/PLAN-APP3-ATS.md §1.4 hallazgo 2): el campo queda fijo, no es una
        // regla a chequear acá.

        hallazgos.Add(new HallazgoAts(
            SeveridadHallazgo.Advertencia, "MANUAL-PARTE-RELACIONADA",
            "\"Parte relacionada\" en ventas queda siempre en \"NO\": Sage 50 no registra ese dato. " +
            "Si alguna venta del período es a una parte relacionada, corregilo a mano en el DIMM antes de declarar."));
        hallazgos.Add(new HallazgoAts(
            SeveridadHallazgo.Advertencia, "MANUAL-RETENCION-SIN-COMPROBANTE",
            "Si un cliente recibió una retención en el período pero no tiene ningún comprobante de venta en " +
            "ese mismo período, esa fila no aparece en el ATS (el `.exe` solo recorre clientes con comprobante). " +
            "Revisá manualmente en el DIMM si corresponde agregarla."));

        return hallazgos;
    }

    private static void ValidarEstructurales(ivaType ats, List<HallazgoAts> hallazgos)
    {
        if (string.IsNullOrWhiteSpace(ats.IdInformante) || ats.IdInformante.Length != 13 || !ats.IdInformante.All(char.IsDigit))
        {
            hallazgos.Add(new HallazgoAts(
                SeveridadHallazgo.Bloqueante, "EST-RUC",
                $"El RUC informante (\"{ats.IdInformante}\") debe tener 13 dígitos numéricos."));
        }

        if (string.IsNullOrWhiteSpace(ats.razonSocial))
        {
            hallazgos.Add(new HallazgoAts(SeveridadHallazgo.Bloqueante, "EST-RAZON-SOCIAL-VACIA", "La razón social no puede estar vacía."));
        }
        else if (!RazonSocialValida.IsMatch(ats.razonSocial))
        {
            hallazgos.Add(new HallazgoAts(
                SeveridadHallazgo.Bloqueante, "EST-RAZON-SOCIAL-CARACTERES",
                $"La razón social (\"{ats.razonSocial}\") tiene caracteres que el ATS no admite (solo letras, números y espacios)."));
        }

        if (!int.TryParse(ats.Mes, out var mes) || mes is < 1 or > 12)
        {
            hallazgos.Add(new HallazgoAts(SeveridadHallazgo.Bloqueante, "EST-MES", $"El mes (\"{ats.Mes}\") debe estar entre 01 y 12."));
        }

        if (!int.TryParse(ats.numEstabRuc, out var numEstab) || numEstab <= 0)
        {
            hallazgos.Add(new HallazgoAts(
                SeveridadHallazgo.Bloqueante, "EST-NUM-ESTAB",
                $"El número de establecimientos (\"{ats.numEstabRuc}\") debe ser mayor a 000."));
        }
    }

    private static void ValidarCruceVentasEstablecimientos(ivaType ats, List<HallazgoAts> hallazgos)
    {
        var ventasEstablecimiento = ats.ventasEstablecimiento ?? Array.Empty<ventaEstType>();

        if (int.TryParse(ats.numEstabRuc, out var numEstab) && ventasEstablecimiento.Length != numEstab)
        {
            hallazgos.Add(new HallazgoAts(
                SeveridadHallazgo.Bloqueante, "CRUCE-NUM-ESTAB",
                $"\"ventasEstablecimiento\" trae {ventasEstablecimiento.Length} registro(s) pero " +
                $"\"numEstabRuc\" declara {ats.numEstabRuc} — el DIMM exige que coincidan."));
        }

        var sumaVentasEstab = ventasEstablecimiento.Sum(v => v.ventasEstab);
        if (sumaVentasEstab > ats.totalVentas + Tolerancia)
        {
            hallazgos.Add(new HallazgoAts(
                SeveridadHallazgo.Bloqueante, "CRUCE-SUMA-VENTAS-ESTAB",
                $"La suma de \"ventasEstablecimiento\" ({sumaVentasEstab:N2}) es mayor que \"totalVentas\" ({ats.totalVentas:N2})."));
        }
    }

    private static void ValidarCompras(ivaType ats, int anio, int mes, List<HallazgoAts> hallazgos)
    {
        var compras = ats.compras ?? Array.Empty<detalleComprasType>();

        for (var i = 0; i < compras.Length; i++)
        {
            var c = compras[i];
            var etiqueta = $"compra #{i + 1} ({c.establecimiento}-{c.puntoEmision}-{c.secuencial}, prov. {c.idProv})";

            if (c.baseImpExe <= 0 && c.baseImponible <= 0 && c.baseImpGrav <= 0)
            {
                hallazgos.Add(new HallazgoAts(
                    SeveridadHallazgo.Bloqueante, "COMPRA-SIN-BASE",
                    $"La {etiqueta} no tiene ninguna base (exenta/imponible/gravada) mayor a 0."));
            }

            var sumaRetencionIva = c.valRetBien10 + c.valRetServ20 + c.valorRetBienes
                + c.valRetServ50 + c.valorRetServicios + c.valRetServ100;
            if (sumaRetencionIva > c.montoIva + Tolerancia)
            {
                hallazgos.Add(new HallazgoAts(
                    SeveridadHallazgo.Bloqueante, "COMPRA-RETENCION-IVA-MAYOR-MONTO-IVA",
                    $"La sumatoria de los valores por concepto de retenciones de IVA ({sumaRetencionIva:N2}) de la {etiqueta} " +
                    $"es mayor al valor de MONTO IVA ({c.montoIva:N2})."));
            }

            ValidarFechasCompra(c, anio, mes, etiqueta, hallazgos);
        }
    }

    private static void ValidarFechasCompra(
        detalleComprasType c, int anio, int mes, string etiqueta, List<HallazgoAts> hallazgos)
    {
        var registro = ParsearFecha(c.fechaRegistro);
        var emision = ParsearFecha(c.fechaEmision);

        if (registro is null || emision is null)
        {
            hallazgos.Add(new HallazgoAts(
                SeveridadHallazgo.Advertencia, "COMPRA-FECHA-INVALIDA",
                $"La {etiqueta} tiene una fecha de registro o emisión con formato inválido " +
                $"(\"{c.fechaRegistro}\" / \"{c.fechaEmision}\")."));
            return;
        }

        if (registro < emision)
        {
            hallazgos.Add(new HallazgoAts(
                SeveridadHallazgo.Advertencia, "COMPRA-FECHA-REGISTRO-ANTERIOR",
                $"La {etiqueta} tiene fecha de registro ({c.fechaRegistro}) anterior a la de emisión ({c.fechaEmision})."));
        }

        if (emision < FechaMinimaSri)
        {
            hallazgos.Add(new HallazgoAts(
                SeveridadHallazgo.Advertencia, "COMPRA-FECHA-MUY-ANTIGUA",
                $"La {etiqueta} tiene fecha de emisión ({c.fechaEmision}) anterior al 01/01/2002."));
        }

        if (emision.Value.Year != anio || emision.Value.Month != mes)
        {
            hallazgos.Add(new HallazgoAts(
                SeveridadHallazgo.Advertencia, "COMPRA-FECHA-FUERA-DE-PERIODO",
                $"La {etiqueta} tiene fecha de emisión ({c.fechaEmision}) fuera del período declarado ({mes:00}/{anio})."));
        }
    }

    private static DateOnly? ParsearFecha(string? texto) =>
        DateOnly.TryParseExact(texto, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
            ? fecha
            : null;

    private static void ValidarDuplicadosCompras(ivaType ats, List<HallazgoAts> hallazgos)
    {
        // Las ventas ya vienen fusionadas por (idCliente, tipoComprobante) en
        // ArmadorVentasAts.Fusionar — no pueden traer duplicados a este nivel.
        var compras = ats.compras ?? Array.Empty<detalleComprasType>();

        var duplicados = compras
            .GroupBy(c => (c.establecimiento, c.puntoEmision, c.secuencial, c.idProv))
            .Where(g => g.Count() > 1);

        foreach (var grupo in duplicados)
        {
            var (establecimiento, puntoEmision, secuencial, idProv) = grupo.Key;
            hallazgos.Add(new HallazgoAts(
                SeveridadHallazgo.Bloqueante, "DUP-COMPRA",
                $"El comprobante {establecimiento}-{puntoEmision}-{secuencial} del proveedor {idProv} " +
                $"aparece {grupo.Count()} veces en compras."));
        }
    }
}
