using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PsaWeb.Compras.Armado;
using PsaWeb.Compras.Catalogo;
using PsaWeb.Compras.Sage;
using PsaWeb.Compras.Sri;
using PsaWeb.Comprobantes.Compras;
using PsaWeb.Conciliacion;
using PsaWeb.Conciliacion.Data;
using PsaWeb.PeachEbills.Data;
using PsaWeb.Recibidos;

namespace PsaWeb.Modules.Compras.Servicios;

/// <summary>Estado de un documento recibido en la bandeja (§13.4 del plan).</summary>
public enum EstadoRecibido
{
    /// <summary>Todavía no hay XML (el WS no lo entregó: fuera de su ventana o error): hay que subirlo a mano.</summary>
    FaltaXml,
    /// <summary>Hay XML y no hay OC en Sage: para registrar.</summary>
    Pendiente,
    /// <summary>Hay OC con esta clave, todavía sin convertir en compra.</summary>
    Guardado,
    /// <summary>La OC ya se convirtió en compra.</summary>
    Registrado,
}

public sealed record DocumentoRecibido(
    string ClaveAcceso,
    string Tipo,
    DateOnly FechaEmision,
    string RucEmisor,
    string Emisor,
    string Serie,
    decimal? Total,
    string? EstadoSri,
    EstadoRecibido Estado,
    int? PostOrder,
    string? ReferenciaOc,
    string? MotivoSinXml);

/// <summary>Formulario precargado desde el XML de una factura recibida.</summary>
public sealed record CargaFacturaRecibida(
    FormularioCompraEstado? Formulario,
    IReadOnlyList<string> Avisos,
    string? Error,
    bool FaltaXml,
    OcGuardadaCabecera? OcExistente,
    string? HuellaXml);

/// <summary>
/// Documentos recibidos del SRI para el módulo Compras (F5): bandeja, formulario desde el XML y aprendizaje de ítems por proveedor.
/// Usa el staging de Conciliación (reporte de la extensión) y el almacén de XML (<c>PsaWeb.Recibidos</c>).
/// </summary>
public sealed class ServicioRecibidos(ServicioCompras compras, IServiceProvider servicios, IDbContextFactory<PeachEbillsContext> peachEbills)
{
    public const string TipoFactura = "Factura";
    public const string TipoNotaCredito = "NotaCredito";

    private IAlmacenXmlRecibidos? Almacen => servicios.GetService<IAlmacenXmlRecibidos>();

    /// <summary>¿Está todo lo necesario (almacén de XML)?</summary>
    public bool Disponible => Almacen is not null;

    public async Task<IReadOnlyList<DocumentoRecibido>> BandejaAsync(string ruc, DateOnly desde, DateOnly hasta, string tipo, CancellationToken ct = default)
    {
        var almacen = Almacen ?? throw new InvalidOperationException("Falta el almacén de XML recibidos.");
        var filas = new Dictionary<string, (DateOnly Fecha, string RucEmisor, string Emisor, string Serie, decimal? Total, string? EstadoSri)>();

        // 1. Reporte del SRI subido por la extensión (staging de Conciliación).
        if (servicios.GetService<ILectorComprobantesSri>() is { } lector)
        {
            var tipoDoc = tipo == TipoFactura ? TipoDocumentoRecibido.Factura : TipoDocumentoRecibido.NotaCredito;
            foreach (var c in (await lector.ObtenerAsync(ruc, desde, hasta, ct)).Where(c => c.Tipo == tipoDoc))
            {
                filas[c.ClaveAcceso] = (c.FechaEmision, c.RucEmisor, c.RazonSocialEmisor, c.SerieComprobante, c.Total, c.Estado);
            }
        }

        // 2. XML cargados «Desde texto» o a mano que no están en el reporte.
        foreach (var x in await almacen.DelPeriodoAsync(ruc, desde, hasta, tipo, ct))
        {
            if (filas.ContainsKey(x.ClaveAcceso)) continue;
            filas[x.ClaveAcceso] = (x.FechaEmision, x.RucEmisor, x.RucEmisor, SerieDeClave(x.ClaveAcceso), null, null);
        }
        if (filas.Count == 0) return [];

        var conXml = await almacen.ConXmlAsync(ruc, filas.Keys, ct);
        var fallidas = servicios.GetService<ServicioDescargaXml>() is { } descarga ? await descarga.FallidasAsync(ruc, ct) : new Dictionary<string, PsaWeb.Recibidos.Data.DescargaXmlFallida>();
        IReadOnlyDictionary<string, (int PostOrder, string Referencia, bool Recibida)> ocs;
        await using (var cn = await compras.ConexionAsync(ruc, ct))
        {
            ocs = await LectorOcs.OcsPorAutorizacionAsync(cn, filas.Keys, ct);
        }

        return filas.Select(f =>
        {
            var oc = ocs.TryGetValue(f.Key, out var o) ? o : ((int, string, bool)?)null;
            var estado = oc is { } x ? (x.Item3 ? EstadoRecibido.Registrado : EstadoRecibido.Guardado)
                : conXml.Contains(f.Key) ? EstadoRecibido.Pendiente : EstadoRecibido.FaltaXml;
            var motivo = estado == EstadoRecibido.FaltaXml && fallidas.TryGetValue(f.Key, out var fa)
                ? (fa.Motivo == ServicioDescargaXml.MotivoSinXml ? "El WS del SRI ya no lo entrega: súbelo a mano." : fa.Detalle ?? fa.Motivo)
                : null;
            return new DocumentoRecibido(f.Key, tipo, f.Value.Fecha, f.Value.RucEmisor, f.Value.Emisor, f.Value.Serie, f.Value.Total,
                f.Value.EstadoSri, estado, oc?.Item1, oc?.Item2, motivo);
        }).OrderBy(d => d.FechaEmision).ThenBy(d => d.Emisor).ToList();
    }

    /// <summary>Baja del WS el XML de las claves que no lo tienen (botón de la bandeja).</summary>
    public Task<ResumenDescargaXml> DescargarAsync(string ruc, IEnumerable<string> claves, string usuario, IProgress<int>? avance = null,
        CancellationToken ct = default) =>
        (servicios.GetService<ServicioDescargaXml>() ?? throw new InvalidOperationException("Falta el servicio de descarga de XML."))
            .DescargarAsync(ruc, claves, usuario, avance: avance, cancellationToken: ct);

    /// <summary>«Desde texto» del `.exe`: toma todas las claves de 49 dígitos de un texto pegado.</summary>
    public static IReadOnlyList<string> ClavesDelTexto(string texto) =>
        Regex.Matches(texto ?? string.Empty, @"(?<!\d)\d{49}(?!\d)").Select(m => m.Value).Distinct().ToList();

    /// <summary>Subida manual de un XML (fuera de la ventana del WS).</summary>
    public Task<GuardadoXml> SubirAsync(string ruc, string contenido, string usuario, string? claveEsperada, bool aceptarOtroReceptor,
        CancellationToken ct = default) =>
        (Almacen ?? throw new InvalidOperationException("Falta el almacén de XML recibidos."))
            .GuardarAsync(ruc, contenido, PsaWeb.Recibidos.Data.OrigenesXml.SubidaManual, usuario, claveEsperada, aceptarOtroReceptor, ct);

    /// <summary>
    /// Formulario de compra desde el XML de la factura (<c>Bill.LoadInfoBill(Factura)</c>). Si no hay XML se intenta el WS; si
    /// tampoco, se pide subirlo. Si ya hay OC con esta clave: la carga en modo actualizar (o solo lectura si ya es compra).
    /// </summary>
    public async Task<CargaFacturaRecibida> CargarFacturaAsync(string ruc, string clave, string usuario, CancellationToken ct = default)
    {
        var almacen = Almacen ?? throw new InvalidOperationException("Falta el almacén de XML recibidos.");
        var xml = await almacen.ObtenerAsync(ruc, clave, ct);
        if (xml is null && servicios.GetService<ServicioDescargaXml>() is { } descarga)
        {
            await descarga.DescargarAsync(ruc, [clave], usuario, cancellationToken: ct);
            xml = await almacen.ObtenerAsync(ruc, clave, ct);
        }
        if (xml is null) return new(null, [], null, true, null, null);

        var lectura = LectorFacturaSri.Leer(xml);
        if (lectura.Factura is not { } factura) return new(null, [], lectura.Error, false, null, null);

        var avisos = new List<string>();
        var contexto = await compras.ContextoAsync(ruc, ct);
        OcGuardadaCabecera? existente = null;
        ProveedorEdicion proveedor;
        await using (var cn = await compras.ConexionAsync(ruc, ct))
        {
            if ((await LectorOcs.OcsPorAutorizacionAsync(cn, [clave], ct)).TryGetValue(clave, out var oc))
            {
                existente = (await LectorOcs.LeerAsync(cn, oc.PostOrder, ct))?.Cabecera;
            }
            var encontrados = await LectorCatalogoCompras.ProveedoresPorIdentificacionAsync(cn, factura.Emisor.Ruc, ct);
            // El `.exe` tomaba el primero (su intento de preferir el activo no funcionaba); se prefiere el activo.
            var elegido = encontrados.FirstOrDefault(p => !p.Inactivo) ?? encontrados.FirstOrDefault();
            if (elegido is not null)
            {
                proveedor = ProveedorEdicion.Desde(ReglasProveedor.ActualizarDesdeFactura(elegido, factura));
                if (encontrados.Count > 1) avisos.Add($"Hay {encontrados.Count} proveedores con la identificación {factura.Emisor.Ruc} en Sage; se usa «{elegido.Id}».");
            }
            else
            {
                var ids = await LectorCatalogoCompras.IdsProveedoresAsync(cn, ct);
                proveedor = ProveedorEdicion.NuevoDesdeFactura(factura, ReglasProveedor.IdPropuesto(factura.Emisor.RazonSocial, ids));
                avisos.Add("El emisor no existe como proveedor en Sage: se creará al guardar (revisa el ID, el email y la cuenta de gasto).");
            }
        }

        IReadOnlyList<ConfiguracionItemProveedor> configs;
        await using (var db = await peachEbills.CreateDbContextAsync(ct))
        {
            configs = await LectorCatalogoPeachEbills.ConfiguracionesAsync(db, ruc, factura.Emisor.Ruc, ct);
        }

        var f = FormularioCompraEstado.DesdeFactura(factura, ruc, proveedor, configs, contexto.Catalogo, out var errores);
        avisos.AddRange(errores);
        if (existente is not null)
        {
            f.Modo = ModoFormulario.Existente;
            f.NumeroOcExistente = existente.Referencia;
            f.NumeroRetencionExistente = existente.Retencion.Length == 0 ? null : existente.Retencion;
            avisos.Add(existente.Recibida
                ? $"Esta factura ya está registrada en Sage ({existente.Referencia}, convertida en compra)."
                : $"Esta factura ya tiene la OC {existente.Referencia}: al guardar se actualiza (conserva su número y su retención).");
        }
        if (factura.EsAmbientePruebas) avisos.Add("La factura es del ambiente de PRUEBAS del SRI.");
        if (factura.Comprador.Identificacion != ruc)
        {
            avisos.Add($"La factura está emitida a otra identificación ({factura.Comprador.Identificacion} — {factura.Comprador.RazonSocial}).");
        }

        // ANULADO (§4.4): se consulta el estado al SRI (no bloquea).
        if (servicios.GetService<IVerificadorEstadoSri>() is { } verificador)
        {
            try
            {
                var estado = await verificador.VerificarAsync(clave, ct);
                if (estado.Estado == EstadoComprobanteSri.Anulado) avisos.Insert(0, "El SRI informa que este comprobante está ANULADO.");
            }
            catch (Exception) when (!ct.IsCancellationRequested) { /* sin estado: no bloquea */ }
        }

        return new(f, avisos, null, false, existente, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(xml))));
    }

    /// <summary>
    /// Aprende «código del proveedor → ítem de Sage» de las líneas con ítem de stock elegido (<c>GetVendorItemConfig</c> +
    /// <c>SettingVendorItemService.SetConfig</c>): nuevas o modificadas. Devuelve cuántas cambió.
    /// </summary>
    public async Task<int> AprenderItemsAsync(string ruc, string identificacionProveedor, IEnumerable<LineaDetalle> detalles,
        CatalogoCompras catalogo, CancellationToken ct = default)
    {
        var candidatas = detalles
            .Where(d => !string.IsNullOrWhiteSpace(d.CodigoPrincipal) && !string.IsNullOrEmpty(d.ItemId)
                        && catalogo.ItemsDetalle.FirstOrDefault(i => i.Id == d.ItemId) is { Clase: 1 })
            .GroupBy(d => d.CodigoPrincipal.Trim())
            .Select(g => (Codigo: g.Key, Item: g.First().ItemId!))
            .ToList();
        if (candidatas.Count == 0) return 0;
        await using var db = await peachEbills.CreateDbContextAsync(ct);
        var existentes = await db.VendorConfiguration
            .Where(x => x.TransmitterRuc == ruc && x.VendorRuc == identificacionProveedor)
            .ToListAsync(ct);
        var cambios = 0;
        foreach (var (codigo, item) in candidatas)
        {
            var e = existentes.FirstOrDefault(x => x.VendorCode == codigo);
            if (e is null)
            {
                db.VendorConfiguration.Add(new VendorConfiguration { TransmitterRuc = ruc, VendorRuc = identificacionProveedor, VendorCode = codigo, SageItemId = item });
                cambios++;
            }
            else if (e.SageItemId != item)
            {
                e.SageItemId = item;
                cambios++;
            }
        }
        if (cambios > 0) await db.SaveChangesAsync(ct);
        return cambios;
    }

    private static string SerieDeClave(string clave) =>
        clave.Length == 49 ? $"{clave.Substring(24, 3)}-{clave.Substring(27, 3)}-{clave.Substring(30, 9)}" : string.Empty;
}
