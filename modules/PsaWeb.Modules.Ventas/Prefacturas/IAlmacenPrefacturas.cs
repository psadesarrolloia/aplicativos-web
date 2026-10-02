using PsaWeb.Ventas.Prefacturas;

namespace PsaWeb.Modules.Ventas.Prefacturas;

/// <summary>Filtro de la lista de prefacturas de una empresa.</summary>
public sealed record FiltroPrefacturas(
    string? Texto = null,
    string? CreadaPor = null,
    EstadoPrefactura? Estado = null,
    DateOnly? Desde = null,
    DateOnly? Hasta = null,
    int Maximo = 200);

/// <summary>Persistencia de prefacturas y de la configuración por empresa.</summary>
public interface IAlmacenPrefacturas
{
    /// <summary>Guarda una prefactura nueva (<c>Id = 0</c>, <c>Numero = 0</c>) asignándole el siguiente número de la empresa; devuelve la guardada.</summary>
    Task<Prefactura> GuardarAsync(Prefactura nueva, CancellationToken cancellationToken = default);

    Task<Prefactura?> ObtenerAsync(string ruc, int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Prefactura>> ListarAsync(string ruc, FiltroPrefacturas filtro, CancellationToken cancellationToken = default);

    Task RegistrarCorreoAsync(string ruc, int id, EstadoCorreo estado, string destinatarios, string? error, DateTime? enviadoEn,
        CancellationToken cancellationToken = default);

    Task MarcarFacturadaAsync(string ruc, int id, string facturaSage, string usuario, DateTime cuando, CancellationToken cancellationToken = default);

    Task CambiarEstadoAsync(string ruc, int id, EstadoPrefactura estado, CancellationToken cancellationToken = default);

    Task<ConfiguracionVentas> ConfiguracionAsync(string ruc, CancellationToken cancellationToken = default);

    Task GuardarConfiguracionAsync(ConfiguracionVentas config, string? usuario, CancellationToken cancellationToken = default);
}
