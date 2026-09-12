using PsaWeb.Ats.Esquema;

namespace PsaWeb.Modules.Ats.Data;

/// <summary>
/// Genera el ATS de un período. Reproduce <c>ATSform</c> del `.exe`
/// (<c>ATSfromPeach</c>): arma el <c>ivaType</c> completo en memoria a partir
/// de Sage 50 (ODBC) + PeachEBills (EF), sin persistir nada.
/// </summary>
public interface IAtsRepository
{
    /// <summary>
    /// Genera el ATS para la empresa de sesión (o la de configuración si no
    /// hay shell).
    /// </summary>
    Task<ivaType> GenerarAsync(FiltroAts filtro, CancellationToken cancellationToken = default);

    /// <summary>
    /// Igual que <see cref="GenerarAsync"/> pero contra una empresa explícita
    /// por RUC. Lo usa el endpoint de exportación, que no tiene el contexto
    /// del circuito Blazor.
    /// </summary>
    Task<ivaType> GenerarParaRucAsync(string ruc, FiltroAts filtro, CancellationToken cancellationToken = default);
}
