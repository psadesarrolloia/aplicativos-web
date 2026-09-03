using Microsoft.EntityFrameworkCore;
using PsaWeb.Comprobantes.Retenciones;
using PsaWeb.Datil;
using PsaWeb.PeachEbills;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.Retenciones.Data;

/// <summary>Datos de conexión a Datil de una empresa + casilla de pruebas.</summary>
public sealed record DatilEmpresa(DatilCredentials Credenciales, string? EmailPruebas);

/// <summary>Lee de PeachEBills los datos del emisor y de Datil por RUC.</summary>
public sealed class EmpresaLookup
{
    private readonly IDbContextFactory<PeachEbillsContext> _contextFactory;

    public EmpresaLookup(IDbContextFactory<PeachEbillsContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<EmpresaEmisora> EmisorAsync(string ruc, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var t = await db.Transmitter.AsNoTracking().FirstOrDefaultAsync(x => x.Ruc == ruc, cancellationToken)
                ?? throw new InvalidOperationException($"No hay empresa (Transmitter) con RUC {ruc}.");

        return new EmpresaEmisora(
            Ruc: t.Ruc,
            RazonSocial: t.Name,
            NombreComercial: t.NameAlias,
            Direccion: t.Address,
            ContribuyenteEspecial: t.NumberResolutionCe ?? string.Empty,
            ObligadoContabilidad: t.HaveToDoAccounting);
    }

    public async Task<DatilEmpresa> DatilAsync(string ruc, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var api = await db.DatilApi.AsNoTracking().FirstOrDefaultAsync(x => x.Ruc == ruc, cancellationToken)
                  ?? throw new InvalidOperationException($"No hay configuración de Datil (DatilAPI) para el RUC {ruc}.");

        var emailPruebas = await db.CurrentAmbient.AsNoTracking()
            .Where(a => a.Ruc == ruc)
            .Select(a => a.EmailForTest)
            .FirstOrDefaultAsync(cancellationToken);

        var credenciales = new DatilCredentials(
            ApiKey: api.MyApiKey,
            Password: DbSecret.Decrypt(api.MySignaturePassword),
            BaseUrl: api.ApiRetencionUrl);

        return new DatilEmpresa(credenciales, emailPruebas);
    }
}
