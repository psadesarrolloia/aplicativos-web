namespace PsaWeb.Comprobantes.Retenciones;

/// <summary>
/// Busca un establecimiento por RUC + código + punto de emisión (tabla
/// <c>Establishments</c> de PeachEBills). Lo implementa la capa que tiene EF.
/// </summary>
public interface IEstablecimientoLookup
{
    Task<EstablecimientoInfo?> BuscarAsync(
        string ruc, string codigo, string puntoEmision, CancellationToken cancellationToken = default);
}

/// <summary>
/// Obtiene la información adicional configurada para un tipo de documento de una
/// empresa (tabla <c>EPoofGeneralAditionalInfo</c>). Lo implementa la capa con EF.
/// </summary>
public interface IInfoAdicionalLookup
{
    Task<IReadOnlyDictionary<string, string>?> ObtenerAsync(
        string ruc, string codDoc, CancellationToken cancellationToken = default);
}
