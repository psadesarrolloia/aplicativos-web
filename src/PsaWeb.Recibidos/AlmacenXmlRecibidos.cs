using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using PsaWeb.Compras.Sri;
using PsaWeb.Recibidos.Data;

namespace PsaWeb.Recibidos;

public enum ResultadoGuardadoXml { Guardado, YaExistia, Conflicto, Rechazado }

/// <param name="Receptor">Identificación del receptor que trae el XML (para confirmar si es de otra empresa).</param>
public sealed record GuardadoXml(ResultadoGuardadoXml Resultado, string? Motivo, string? ClaveAcceso = null, string? Receptor = null);

/// <summary>Datos que se sacan del comprobante para validar y catalogar el XML.</summary>
public sealed record DatosComprobante(TipoComprobanteSri Tipo, string ClaveAcceso, string RucEmisor, string Receptor,
    DateOnly FechaEmision, DateTime? FechaAutorizacion);

public interface IAlmacenXmlRecibidos
{
    /// <summary>Valida y guarda (§13.1). Con <paramref name="claveEsperada"/> se exige que el XML sea de esa clave.</summary>
    Task<GuardadoXml> GuardarAsync(string ruc, string contenido, string origen, string usuario, string? claveEsperada = null,
        bool aceptarOtroReceptor = false, CancellationToken cancellationToken = default);

    /// <summary>El XML tal como llegó, o <c>null</c>.</summary>
    Task<string?> ObtenerAsync(string ruc, string claveAcceso, CancellationToken cancellationToken = default);

    /// <summary>De esas claves, las que ya tienen XML.</summary>
    Task<IReadOnlySet<string>> ConXmlAsync(string ruc, IEnumerable<string> claves, CancellationToken cancellationToken = default);

    /// <summary>XML guardados de un período (por fecha de emisión) para un tipo.</summary>
    Task<IReadOnlyList<XmlComprobanteRecibido>> DelPeriodoAsync(string ruc, DateOnly desde, DateOnly hasta, string tipo,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Almacén de XML de comprobantes recibidos (§13.1): el servidor no confía en quien sube — el XML debe parsear, su
/// <c>claveAcceso</c> interna debe ser la esperada, el receptor debe ser la empresa (salvo confirmación explícita, como el
/// «factura para otra empresa» del `.exe`) y debe ser factura, nota de crédito o retención. Inmutable: una clave se guarda una
/// vez; un XML distinto para la misma clave queda como conflicto.
/// </summary>
public sealed class AlmacenXmlRecibidos(IDbContextFactory<RecibidosDbContext> contextos, TimeProvider? reloj = null) : IAlmacenXmlRecibidos
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    public async Task<GuardadoXml> GuardarAsync(string ruc, string contenido, string origen, string usuario, string? claveEsperada = null,
        bool aceptarOtroReceptor = false, CancellationToken cancellationToken = default)
    {
        var (datos, error) = Analizar(contenido);
        if (datos is null) return new(ResultadoGuardadoXml.Rechazado, error);
        if (claveEsperada is not null && datos.ClaveAcceso != claveEsperada)
        {
            return new(ResultadoGuardadoXml.Rechazado, $"El XML es de otra clave de acceso ({datos.ClaveAcceso}).", datos.ClaveAcceso);
        }
        if (datos.Receptor != ruc && !aceptarOtroReceptor)
        {
            return new(ResultadoGuardadoXml.Rechazado, $"El comprobante está emitido a otra identificación ({datos.Receptor}), no a la empresa.",
                datos.ClaveAcceso, datos.Receptor);
        }

        var hash = Hash(contenido);
        await using var db = await contextos.CreateDbContextAsync(cancellationToken);
        var existente = await db.Xmls.AsNoTracking()
            .Where(x => x.Ruc == ruc && x.ClaveAcceso == datos.ClaveAcceso)
            .Select(x => new { x.HashSha256 })
            .FirstOrDefaultAsync(cancellationToken);
        if (existente is not null)
        {
            if (existente.HashSha256 == hash) return new(ResultadoGuardadoXml.YaExistia, null, datos.ClaveAcceso, datos.Receptor);
            // Mismo comprobante envuelto distinto (respuesta SOAP vs. archivo del portal) no es un conflicto real.
            var guardado = await ObtenerAsync(ruc, datos.ClaveAcceso, cancellationToken);
            if (guardado is not null && MismoComprobante(guardado, contenido))
            {
                return new(ResultadoGuardadoXml.YaExistia, null, datos.ClaveAcceso, datos.Receptor);
            }
            db.Conflictos.Add(new ConflictoXmlRecibido
            {
                Ruc = ruc, ClaveAcceso = datos.ClaveAcceso, Xml = Comprimir(contenido), HashSha256 = hash, Origen = origen,
                SubidoPor = usuario, FechaUtc = _reloj.GetUtcNow().UtcDateTime,
            });
            await db.SaveChangesAsync(cancellationToken);
            return new(ResultadoGuardadoXml.Conflicto, "Ya había otro XML distinto para esta clave: se conserva el primero y este queda para revisión.",
                datos.ClaveAcceso, datos.Receptor);
        }

        db.Xmls.Add(new XmlComprobanteRecibido
        {
            Ruc = ruc,
            ClaveAcceso = datos.ClaveAcceso,
            TipoComprobante = datos.Tipo.ToString(),
            RucEmisor = datos.RucEmisor,
            FechaEmision = datos.FechaEmision,
            FechaAutorizacion = datos.FechaAutorizacion,
            Xml = Comprimir(contenido),
            HashSha256 = hash,
            Origen = origen,
            SubidoPor = usuario,
            FechaCargaUtc = _reloj.GetUtcNow().UtcDateTime,
        });
        await db.DescargasFallidas.Where(x => x.Ruc == ruc && x.ClaveAcceso == datos.ClaveAcceso).ExecuteDeleteAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Carrera con otra carga de la misma clave: la otra ganó.
            return new(ResultadoGuardadoXml.YaExistia, null, datos.ClaveAcceso, datos.Receptor);
        }
        return new(ResultadoGuardadoXml.Guardado, null, datos.ClaveAcceso, datos.Receptor);
    }

    public async Task<string?> ObtenerAsync(string ruc, string claveAcceso, CancellationToken cancellationToken = default)
    {
        await using var db = await contextos.CreateDbContextAsync(cancellationToken);
        var xml = await db.Xmls.AsNoTracking().Where(x => x.Ruc == ruc && x.ClaveAcceso == claveAcceso)
            .Select(x => x.Xml).FirstOrDefaultAsync(cancellationToken);
        return xml is null ? null : Descomprimir(xml);
    }

    public async Task<IReadOnlySet<string>> ConXmlAsync(string ruc, IEnumerable<string> claves, CancellationToken cancellationToken = default)
    {
        var lista = claves.Distinct().ToList();
        var resultado = new HashSet<string>();
        await using var db = await contextos.CreateDbContextAsync(cancellationToken);
        foreach (var lote in lista.Chunk(500))
        {
            resultado.UnionWith(await db.Xmls.AsNoTracking().Where(x => x.Ruc == ruc && lote.Contains(x.ClaveAcceso))
                .Select(x => x.ClaveAcceso).ToListAsync(cancellationToken));
        }
        return resultado;
    }

    public async Task<IReadOnlyList<XmlComprobanteRecibido>> DelPeriodoAsync(string ruc, DateOnly desde, DateOnly hasta, string tipo,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextos.CreateDbContextAsync(cancellationToken);
        return await db.Xmls.AsNoTracking()
            .Where(x => x.Ruc == ruc && x.TipoComprobante == tipo && x.FechaEmision >= desde && x.FechaEmision <= hasta)
            .Select(x => new XmlComprobanteRecibido
            {
                Id = x.Id, Ruc = x.Ruc, ClaveAcceso = x.ClaveAcceso, TipoComprobante = x.TipoComprobante, RucEmisor = x.RucEmisor,
                FechaEmision = x.FechaEmision, FechaAutorizacion = x.FechaAutorizacion, Origen = x.Origen, FechaCargaUtc = x.FechaCargaUtc,
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>Tipo, clave, emisor, receptor y fechas del comprobante; o el motivo por el que no es válido.</summary>
    public static (DatosComprobante? Datos, string? Error) Analizar(string contenido)
    {
        var c = LectorComprobanteSri.Leer(contenido);
        if (c.Tipo == TipoComprobanteSri.NoValido || c.Raiz is null) return (null, c.Error ?? "Comprobante no válido.");
        if (c.Tipo == TipoComprobanteSri.NotaDebito) return (null, "Las notas de débito no se registran en este módulo.");
        var trib = c.Raiz.Element("infoTributaria");
        var info = c.Tipo switch
        {
            TipoComprobanteSri.Factura => c.Raiz.Element("infoFactura"),
            TipoComprobanteSri.NotaCredito => c.Raiz.Element("infoNotaCredito"),
            _ => c.Raiz.Element("infoCompRetencion"),
        };
        var clave = (string?)trib?.Element("claveAcceso") ?? string.Empty;
        if (clave.Length != 49) return (null, "El comprobante no trae una clave de acceso válida.");
        var receptor = (string?)(c.Tipo == TipoComprobanteSri.Retencion
            ? info?.Element("identificacionSujetoRetenido")
            : info?.Element("identificacionComprador")) ?? string.Empty;
        if (!DateOnly.TryParseExact((string?)info?.Element("fechaEmision"), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
        {
            return (null, "El comprobante no trae una fecha de emisión válida.");
        }
        DateTime? autorizacion = null;
        try
        {
            var doc = XDocument.Parse(contenido.TrimStart('﻿'));
            var texto = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "fechaAutorizacion")?.Value;
            if (DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fa)) autorizacion = fa.UtcDateTime;
        }
        catch (System.Xml.XmlException) { }
        return (new DatosComprobante(c.Tipo, clave, ((string?)trib?.Element("ruc") ?? string.Empty).Trim(), receptor.Trim(), fecha, autorizacion), null);
    }

    private static bool MismoComprobante(string a, string b)
    {
        var ca = LectorComprobanteSri.Leer(a).Raiz;
        var cb = LectorComprobanteSri.Leer(b).Raiz;
        return ca is not null && cb is not null && XNode.DeepEquals(Normalizar(ca), Normalizar(cb));
    }

    private static XElement Normalizar(XElement e) =>
        new(e.Name, e.Attributes().Where(a => !a.IsNamespaceDeclaration),
            e.Elements().Any() ? e.Elements().Select(Normalizar) : (object)e.Value.Trim());

    internal static string Hash(string texto) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));

    internal static byte[] Comprimir(string texto)
    {
        using var salida = new MemoryStream();
        using (var gz = new GZipStream(salida, CompressionLevel.Optimal))
        {
            var bytes = Encoding.UTF8.GetBytes(texto);
            gz.Write(bytes, 0, bytes.Length);
        }
        return salida.ToArray();
    }

    internal static string Descomprimir(byte[] datos)
    {
        using var entrada = new GZipStream(new MemoryStream(datos), CompressionMode.Decompress);
        using var lector = new StreamReader(entrada, Encoding.UTF8);
        return lector.ReadToEnd();
    }
}
