using PsaWeb.Ventas.Prefacturas;

namespace PsaWeb.Modules.Ventas.Prefacturas;

/// <summary>Implementación en memoria (desarrollo sin <c>Plataforma:ConnectionString</c> y pruebas).</summary>
public sealed class AlmacenPrefacturasMemoria : IAlmacenPrefacturas
{
    private readonly object _candado = new();
    private readonly List<Prefactura> _prefacturas = new();
    private readonly Dictionary<string, ConfiguracionVentas> _configuraciones = new();
    private int _siguienteId = 1;

    public Task<Prefactura> GuardarAsync(Prefactura nueva, CancellationToken cancellationToken = default)
    {
        lock (_candado)
        {
            var numero = _prefacturas.Where(p => p.Ruc == nueva.Ruc).Select(p => p.Numero).DefaultIfEmpty(0).Max() + 1;
            var guardada = nueva with { Id = _siguienteId++, Numero = numero };
            _prefacturas.Add(guardada);
            return Task.FromResult(guardada);
        }
    }

    public Task<Prefactura?> ObtenerAsync(string ruc, int id, CancellationToken cancellationToken = default)
    {
        lock (_candado) return Task.FromResult(_prefacturas.FirstOrDefault(p => p.Ruc == ruc && p.Id == id));
    }

    public Task<IReadOnlyList<Prefactura>> ListarAsync(string ruc, FiltroPrefacturas filtro, CancellationToken cancellationToken = default)
    {
        lock (_candado)
        {
            var q = _prefacturas.Where(p => p.Ruc == ruc);
            if (!string.IsNullOrWhiteSpace(filtro.CreadaPor)) q = q.Where(p => p.CreadaPor == filtro.CreadaPor);
            if (filtro.Estado is { } e) q = q.Where(p => p.Estado == e);
            if (filtro.Desde is { } d) q = q.Where(p => p.FechaEmision >= d);
            if (filtro.Hasta is { } h) q = q.Where(p => p.FechaEmision <= h);
            if (!string.IsNullOrWhiteSpace(filtro.Texto))
            {
                var t = filtro.Texto.Trim();
                q = q.Where(p => p.ClienteNombre.Contains(t, StringComparison.OrdinalIgnoreCase) || p.ClienteId.Contains(t, StringComparison.OrdinalIgnoreCase)
                                 || p.Vendedor.Contains(t, StringComparison.OrdinalIgnoreCase) || (p.FacturaSage?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false));
            }
            return Task.FromResult<IReadOnlyList<Prefactura>>(q.OrderByDescending(p => p.Numero).Take(filtro.Maximo).ToList());
        }
    }

    public Task RegistrarCorreoAsync(string ruc, int id, EstadoCorreo estado, string destinatarios, string? error, DateTime? enviadoEn,
        CancellationToken cancellationToken = default)
    {
        Reemplazar(ruc, id, p => p with { CorreoEstado = estado, CorreoDestinatarios = destinatarios, CorreoError = error, CorreoEnviadoEn = enviadoEn ?? p.CorreoEnviadoEn });
        return Task.CompletedTask;
    }

    public Task MarcarFacturadaAsync(string ruc, int id, string facturaSage, string usuario, DateTime cuando, CancellationToken cancellationToken = default)
    {
        Reemplazar(ruc, id, p => p with { Estado = EstadoPrefactura.Facturada, FacturaSage = facturaSage.Trim(), FacturadaPor = usuario, FacturadaEn = cuando });
        return Task.CompletedTask;
    }

    public Task CambiarEstadoAsync(string ruc, int id, EstadoPrefactura estado, CancellationToken cancellationToken = default)
    {
        Reemplazar(ruc, id, p => p with { Estado = estado });
        return Task.CompletedTask;
    }

    public Task<ConfiguracionVentas> ConfiguracionAsync(string ruc, CancellationToken cancellationToken = default)
    {
        lock (_candado) return Task.FromResult(_configuraciones.TryGetValue(ruc, out var c) ? c : ConfiguracionVentas.PorDefecto(ruc));
    }

    public Task GuardarConfiguracionAsync(ConfiguracionVentas config, string? usuario, CancellationToken cancellationToken = default)
    {
        lock (_candado) _configuraciones[config.Ruc] = config;
        return Task.CompletedTask;
    }

    private void Reemplazar(string ruc, int id, Func<Prefactura, Prefactura> cambio)
    {
        lock (_candado)
        {
            var i = _prefacturas.FindIndex(p => p.Ruc == ruc && p.Id == id);
            if (i < 0) throw new InvalidOperationException("La prefactura no existe.");
            _prefacturas[i] = cambio(_prefacturas[i]);
        }
    }
}
