using Microsoft.EntityFrameworkCore;

namespace PsaWeb.Conciliacion.Data;

public interface IRepositorioRevisionesConciliacion
{
    /// <summary>Todas las revisiones de una empresa, por <see cref="RevisionConciliacion.Clave"/>.</summary>
    Task<IReadOnlyDictionary<string, RevisionConciliacion>> ListarAsync(string ruc, CancellationToken cancellationToken = default);

    /// <summary>Marca una fila como revisada (o reemplaza la revisión anterior de esa misma fila).</summary>
    /// <exception cref="ArgumentException">El comentario está vacío o excede <see cref="ClaveRevision.MaxComentario"/>.</exception>
    Task RegistrarAsync(
        string ruc, string clave, string huella, string comentario, string revisadaPor,
        CancellationToken cancellationToken = default);

    /// <summary>Deshace una revisión marcada por error. No falla si la fila no tenía revisión.</summary>
    Task QuitarAsync(string ruc, string clave, CancellationToken cancellationToken = default);
}

public sealed class RepositorioRevisionesConciliacion(ConciliacionDbContext db) : IRepositorioRevisionesConciliacion
{
    public async Task<IReadOnlyDictionary<string, RevisionConciliacion>> ListarAsync(
        string ruc, CancellationToken cancellationToken = default) =>
        await db.RevisionesConciliacion
            .AsNoTracking()
            .Where(r => r.Ruc == ruc)
            .ToDictionaryAsync(r => r.Clave, cancellationToken);

    public async Task RegistrarAsync(
        string ruc, string clave, string huella, string comentario, string revisadaPor,
        CancellationToken cancellationToken = default)
    {
        comentario = comentario?.Trim() ?? string.Empty;
        if (comentario.Length == 0)
        {
            throw new ArgumentException("El comentario es obligatorio para aceptar una diferencia.", nameof(comentario));
        }

        if (comentario.Length > ClaveRevision.MaxComentario)
        {
            throw new ArgumentException($"El comentario no puede pasar de {ClaveRevision.MaxComentario} caracteres.", nameof(comentario));
        }

        var existente = await db.RevisionesConciliacion
            .FirstOrDefaultAsync(r => r.Ruc == ruc && r.Clave == clave, cancellationToken);
        if (existente is null)
        {
            db.RevisionesConciliacion.Add(new RevisionConciliacion
            {
                Ruc = ruc, Clave = clave, Huella = huella, Comentario = comentario,
                RevisadaPor = revisadaPor, RevisadaUtc = DateTime.UtcNow,
            });
        }
        else
        {
            existente.Huella = huella;
            existente.Comentario = comentario;
            existente.RevisadaPor = revisadaPor;
            existente.RevisadaUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task QuitarAsync(string ruc, string clave, CancellationToken cancellationToken = default) =>
        await db.RevisionesConciliacion
            .Where(r => r.Ruc == ruc && r.Clave == clave)
            .ExecuteDeleteAsync(cancellationToken);
}
