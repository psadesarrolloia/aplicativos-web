using Microsoft.EntityFrameworkCore;
using PsaWeb.Ventas.Prefacturas;

namespace PsaWeb.Modules.Ventas.Prefacturas;

/// <summary>Implementación persistente (PsaWebPlataforma). Un contexto por operación (<see cref="IDbContextFactory{TContext}"/>) porque los circuitos de Blazor son largos.</summary>
public sealed class AlmacenPrefacturasEf(IDbContextFactory<VentasDbContext> fabrica) : IAlmacenPrefacturas
{
    private const int IntentosDeNumeracion = 25;

    public async Task<Prefactura> GuardarAsync(Prefactura nueva, CancellationToken cancellationToken = default)
    {
        for (var intento = 1; ; intento++)
        {
            await using var db = await fabrica.CreateDbContextAsync(cancellationToken);
            var siguiente = (await db.Prefacturas.Where(x => x.Ruc == nueva.Ruc).MaxAsync(x => (int?)x.Numero, cancellationToken) ?? 0) + 1;
            var entidad = Mapa.AEntidad(nueva with { Numero = siguiente });
            db.Prefacturas.Add(entidad);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return Mapa.ADominio(entidad);
            }
            catch (DbUpdateException) when (intento < IntentosDeNumeracion)
            {
                // Otro vendedor tomó el mismo número a la vez (índice único Ruc+Numero): se recalcula y se reintenta.
                await Task.Delay(Random.Shared.Next(5, 40), cancellationToken);
            }
        }
    }

    public async Task<Prefactura?> ObtenerAsync(string ruc, int id, CancellationToken cancellationToken = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(cancellationToken);
        var e = await db.Prefacturas.AsNoTracking().Include(x => x.Lineas).FirstOrDefaultAsync(x => x.Ruc == ruc && x.Id == id, cancellationToken);
        return e is null ? null : Mapa.ADominio(e);
    }

    public async Task<IReadOnlyList<Prefactura>> ListarAsync(string ruc, FiltroPrefacturas filtro, CancellationToken cancellationToken = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(cancellationToken);
        var q = db.Prefacturas.AsNoTracking().Include(x => x.Lineas).Where(x => x.Ruc == ruc);
        if (!string.IsNullOrWhiteSpace(filtro.CreadaPor)) q = q.Where(x => x.CreadaPor == filtro.CreadaPor);
        if (filtro.Estado is { } estado) q = q.Where(x => x.Estado == (int)estado);
        if (filtro.Desde is { } d) { var desde = d.ToDateTime(TimeOnly.MinValue); q = q.Where(x => x.FechaEmision >= desde); }
        if (filtro.Hasta is { } h) { var hasta = h.ToDateTime(TimeOnly.MinValue); q = q.Where(x => x.FechaEmision <= hasta); }
        if (!string.IsNullOrWhiteSpace(filtro.Texto))
        {
            var t = filtro.Texto.Trim();
            q = q.Where(x => x.ClienteNombre.Contains(t) || x.ClienteId.Contains(t) || x.Vendedor.Contains(t) || (x.FacturaSage != null && x.FacturaSage.Contains(t)));
        }
        var lista = await q.OrderByDescending(x => x.Numero).Take(Math.Clamp(filtro.Maximo, 1, 1000)).ToListAsync(cancellationToken);
        return lista.Select(Mapa.ADominio).ToList();
    }

    public async Task RegistrarCorreoAsync(string ruc, int id, EstadoCorreo estado, string destinatarios, string? error, DateTime? enviadoEn,
        CancellationToken cancellationToken = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(cancellationToken);
        var e = await db.Prefacturas.FirstOrDefaultAsync(x => x.Ruc == ruc && x.Id == id, cancellationToken) ?? throw new InvalidOperationException("La prefactura no existe.");
        e.CorreoEstado = (int)estado;
        e.CorreoDestinatarios = Recortar(destinatarios, 500);
        e.CorreoError = error is null ? null : Recortar(error, 1000);
        if (enviadoEn is not null) e.CorreoEnviadoEn = enviadoEn;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarcarFacturadaAsync(string ruc, int id, string facturaSage, string usuario, DateTime cuando, CancellationToken cancellationToken = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(cancellationToken);
        var e = await db.Prefacturas.FirstOrDefaultAsync(x => x.Ruc == ruc && x.Id == id, cancellationToken) ?? throw new InvalidOperationException("La prefactura no existe.");
        e.Estado = (int)EstadoPrefactura.Facturada;
        e.FacturaSage = Recortar(facturaSage.Trim(), 40);
        e.FacturadaPor = Recortar(usuario, 100);
        e.FacturadaEn = cuando;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CambiarEstadoAsync(string ruc, int id, EstadoPrefactura estado, CancellationToken cancellationToken = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(cancellationToken);
        var e = await db.Prefacturas.FirstOrDefaultAsync(x => x.Ruc == ruc && x.Id == id, cancellationToken) ?? throw new InvalidOperationException("La prefactura no existe.");
        e.Estado = (int)estado;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ConfiguracionVentas> ConfiguracionAsync(string ruc, CancellationToken cancellationToken = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(cancellationToken);
        var e = await db.ConfiguracionesVentas.AsNoTracking().FirstOrDefaultAsync(x => x.Ruc == ruc, cancellationToken);
        return e is null
            ? ConfiguracionVentas.PorDefecto(ruc)
            : new ConfiguracionVentas(e.Ruc, e.CorreoContabilidad, e.CorreoAdicional, e.VigenciaDias, e.CodigoImpuesto, e.PorcentajeIva);
    }

    public async Task GuardarConfiguracionAsync(ConfiguracionVentas config, string? usuario, CancellationToken cancellationToken = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(cancellationToken);
        var e = await db.ConfiguracionesVentas.FirstOrDefaultAsync(x => x.Ruc == config.Ruc, cancellationToken);
        if (e is null)
        {
            e = new ConfiguracionVentasEntidad { Ruc = config.Ruc };
            db.ConfiguracionesVentas.Add(e);
        }
        e.CorreoContabilidad = config.CorreoContabilidad.Trim();
        e.CorreoAdicional = config.CorreoAdicional.Trim();
        e.VigenciaDias = config.VigenciaDias;
        e.CodigoImpuesto = config.CodigoImpuesto.Trim();
        e.PorcentajeIva = config.PorcentajeIva;
        e.ActualizadoPor = usuario;
        e.ActualizadoEn = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>Traducción entre las entidades de EF y el modelo de dominio.</summary>
internal static class Mapa
{
    public static PrefacturaEntidad AEntidad(Prefactura p) => new()
    {
        Id = p.Id, Ruc = p.Ruc, EmpresaNombre = p.EmpresaNombre, Numero = p.Numero,
        FechaEmision = p.FechaEmision.ToDateTime(TimeOnly.MinValue), ValidaHasta = p.ValidaHasta.ToDateTime(TimeOnly.MinValue),
        Estado = (int)p.Estado, ClienteId = p.ClienteId, ClienteNombre = p.ClienteNombre, ClienteContacto = p.ClienteContacto,
        ClienteTelefono = p.ClienteTelefono, ClienteEmail = p.ClienteEmail, ListaDePrecios = p.ListaDePrecios, DiasCredito = p.DiasCredito,
        Terminos = p.Terminos, Vendedor = p.Vendedor, Etiqueta = p.Etiqueta, OrdenCliente = p.OrdenCliente, DireccionEnvio = p.DireccionEnvio,
        NotaCliente = p.NotaCliente, NotaInterna = p.NotaInterna, CodigoImpuesto = p.CodigoImpuesto, PorcentajeIva = p.PorcentajeIva,
        Subtotal = p.Subtotal, Iva = p.Iva, Total = p.Total, CreadaPor = p.CreadaPor, CreadaEn = p.CreadaEn,
        CorreoEstado = (int)p.CorreoEstado, CorreoDestinatarios = p.CorreoDestinatarios, CorreoError = p.CorreoError, CorreoEnviadoEn = p.CorreoEnviadoEn,
        FacturaSage = p.FacturaSage, FacturadaPor = p.FacturadaPor, FacturadaEn = p.FacturadaEn,
        Lineas = p.Lineas.Select(l => new PrefacturaLineaEntidad
        {
            Orden = l.Orden, ItemId = l.ItemId, Descripcion = l.Descripcion, UnidadMedida = l.UnidadMedida, Cantidad = l.Cantidad,
            PrecioLista = l.PrecioLista, PrecioUnitario = l.PrecioUnitario, PrecioManual = l.PrecioManual, Monto = l.Monto,
            ExistenciaAlEmitir = l.ExistenciaAlEmitir,
        }).ToList(),
    };

    public static Prefactura ADominio(PrefacturaEntidad e) => new(
        e.Id, e.Ruc, e.EmpresaNombre, e.Numero, DateOnly.FromDateTime(e.FechaEmision), DateOnly.FromDateTime(e.ValidaHasta), (EstadoPrefactura)e.Estado,
        e.ClienteId, e.ClienteNombre, e.ClienteContacto, e.ClienteTelefono, e.ClienteEmail, e.ListaDePrecios, e.DiasCredito, e.Terminos,
        e.Vendedor, e.Etiqueta, e.OrdenCliente, e.DireccionEnvio, e.NotaCliente, e.NotaInterna, e.CodigoImpuesto, e.PorcentajeIva,
        e.Subtotal, e.Iva, e.Total,
        e.Lineas.OrderBy(l => l.Orden).Select(l => new LineaPrefactura(l.Orden, l.ItemId, l.Descripcion, l.UnidadMedida, l.Cantidad, l.PrecioLista,
            l.PrecioUnitario, l.PrecioManual, l.Monto, l.ExistenciaAlEmitir)).ToList(),
        e.CreadaPor, e.CreadaEn, (EstadoCorreo)e.CorreoEstado, e.CorreoDestinatarios, e.CorreoError, e.CorreoEnviadoEn,
        e.FacturaSage, e.FacturadaPor, e.FacturadaEn);
}
