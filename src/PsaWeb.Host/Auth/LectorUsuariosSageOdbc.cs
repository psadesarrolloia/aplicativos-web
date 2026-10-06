using System.Data.Odbc;
using Microsoft.Extensions.Caching.Memory;
using PsaWeb.PeachEbills;
using PsaWeb.Seguridad;

namespace PsaWeb.Host.Auth;

/// <summary>
/// <see cref="ILectorUsuariosSage"/> por ODBC: <c>SELECT DISTINCT UserID FROM UserPreference</c> de la compañía del RUC (cadena de
/// <c>PeachConnString</c>). Solo lectura; caché de 10 minutos por RUC. Descarta el usuario genérico <c>default_user</c>.
/// </summary>
public sealed class LectorUsuariosSageOdbc(PeachConnStringResolver resolver, IMemoryCache cache, ILogger<LectorUsuariosSageOdbc> log)
    : ILectorUsuariosSage
{
    private static readonly HashSet<string> Genericos = new(StringComparer.OrdinalIgnoreCase) { "default_user" };

    public async Task<UsuariosSage> UsuariosAsync(string ruc, CancellationToken cancellationToken = default)
    {
        var clave = "usuarios-sage:" + ruc;
        if (cache.TryGetValue(clave, out UsuariosSage? guardado) && guardado is not null) return guardado;
        UsuariosSage resultado;
        try
        {
            await using var cn = new OdbcConnection(await resolver.ResolverCadenaOdbcAsync(ruc, cancellationToken));
            await cn.OpenAsync(cancellationToken);
            await using var cmd = new OdbcCommand("SELECT DISTINCT UserID FROM \"UserPreference\"", cn);
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            var lista = new List<string>();
            while (await rd.ReadAsync(cancellationToken))
            {
                var u = rd.IsDBNull(0) ? "" : rd.GetValue(0)?.ToString()?.Trim() ?? "";
                if (u.Length > 0 && !Genericos.Contains(u)) lista.Add(u);
            }
            resultado = new UsuariosSage(lista.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(), null);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "No se pudieron leer los usuarios de Sage de {Ruc}", ruc);
            resultado = new UsuariosSage(Array.Empty<string>(), "No se pudo leer la compañía en Sage: " + ex.Message);
        }
        cache.Set(clave, resultado, resultado.Error is null ? TimeSpan.FromMinutes(10) : TimeSpan.FromMinutes(1));
        return resultado;
    }
}
