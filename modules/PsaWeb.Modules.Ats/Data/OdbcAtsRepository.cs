using Microsoft.EntityFrameworkCore;
using System.Data.Odbc;
using PsaWeb.Ats;
using PsaWeb.Ats.Anulados;
using PsaWeb.Ats.Compras;
using PsaWeb.Ats.Esquema;
using PsaWeb.Ats.Ventas;
using PsaWeb.PeachEbills.Data;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.Ats.Data;

/// <summary>
/// Implementación real: arma el <c>ivaType</c> leyendo Sage 50 (ODBC, vía los
/// lectores de <c>PsaWeb.Ats</c>) y PeachEBills (EF, para establecimientos
/// activos, razón social y el diccionario <c>dicIdentityTypeATS</c>). Port de
/// <c>LoadATS.LoadToATSobject</c>.
/// </summary>
internal sealed class OdbcAtsRepository(
    ISageConnectionFactory connections,
    IResolverEmpresaSage resolver,
    IDbContextFactory<PeachEbillsContext> contextFactory)
    : IAtsRepository
{
    public async Task<ivaType> GenerarAsync(FiltroAts filtro, CancellationToken cancellationToken = default)
    {
        var ruc = resolver.RucSesion
            ?? throw new InvalidOperationException("No hay empresa de sesión seleccionada.");
        return await EjecutarAsync(ruc, await CadenaSesionAsync(cancellationToken), filtro, cancellationToken);
    }

    public async Task<ivaType> GenerarParaRucAsync(
        string ruc, FiltroAts filtro, CancellationToken cancellationToken = default)
        => await EjecutarAsync(ruc, await resolver.CadenaOdbcAsync(ruc, cancellationToken), filtro, cancellationToken);

    private async Task<string?> CadenaSesionAsync(CancellationToken ct)
        => resolver.RucSesion is { } ruc ? await resolver.CadenaOdbcAsync(ruc, ct) : null;

    private async Task<ivaType> EjecutarAsync(string ruc, string? cadena, FiltroAts filtro, CancellationToken ct)
    {
        var (razonSocial, establecimientosActivos, catalogoIdentificacion) = await LeerPeachEbillsAsync(ruc, ct);

        var cn = cadena is null ? connections.CreateConnection() : connections.CreateConnection(cadena);
        await using (cn)
        {
            await cn.OpenAsync(ct);

            var filasVenta = await LectorVentasAts.LeerAsync(cn, filtro.Anio, filtro.Mes, ct);
            var ventas = ArmadorVentasAts.Fusionar(filasVenta);
            var ventasPorEstablecimiento = await LectorVentasEstablecimientoAts.LeerAsync(cn, filtro.Anio, filtro.Mes, ct);
            var compras = await LectorComprasAts.LeerAsync(cn, filtro.Anio, filtro.Mes, catalogoIdentificacion, ct);
            var anulados = await LectorAnuladosAts.LeerAsync(cn, filtro.Anio, filtro.Mes, ct);

            var info = new InformacionAts(
                Ruc: ruc,
                RazonSocial: razonSocial,
                Anio: filtro.Anio,
                Mes: filtro.Mes,
                EstablecimientosActivos: establecimientosActivos,
                Ventas: ventas,
                VentasPorEstablecimientoCrudo: ventasPorEstablecimiento,
                Compras: compras,
                Anulados: anulados);

            return ArmadorAts.Armar(info);
        }
    }

    private async Task<(string RazonSocial, IReadOnlyList<string> EstablecimientosActivos, IReadOnlyList<CodigoIdentificacionCompras> CatalogoIdentificacion)>
        LeerPeachEbillsAsync(string ruc, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var razonSocial = await db.Transmitter.AsNoTracking()
            .Where(t => t.Ruc == ruc)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"No hay empresa (Transmitter) con RUC {ruc}.");

        var establecimientosActivos = await db.Establishments.AsNoTracking()
            .Where(e => e.Ruc == ruc && e.IsFromPeach)
            .Select(e => e.Code)
            .Distinct()
            .ToListAsync(ct);

        var catalogoIdentificacion = await db.DicIdentityTypeAts.AsNoTracking()
            .Where(d => d.TransType == 2)
            .Select(d => new CodigoIdentificacionCompras(d.ProofTypeId, d.IdAts))
            .ToListAsync(ct);

        return (razonSocial, establecimientosActivos, catalogoIdentificacion);
    }
}
