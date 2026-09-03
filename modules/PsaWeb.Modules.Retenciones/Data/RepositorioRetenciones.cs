using Microsoft.EntityFrameworkCore;
using PsaWeb.Comprobantes.Proveedores;
using PsaWeb.Comprobantes.Retenciones;
using PsaWeb.Datil;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Modules.Retenciones.Data;

/// <summary>
/// Persiste una retención emitida en PeachEBills: upsert de <c>Persons</c> y, en
/// una transacción, alta de <c>TaxWithHoldings</c> + <c>THDetails</c> +
/// <c>DatilRequests</c>. Port de <c>CRUDsql.TaxWithHCreate</c>.
/// </summary>
public sealed class RepositorioRetenciones
{
    private readonly IDbContextFactory<PeachEbillsContext> _contextFactory;

    public RepositorioRetenciones(IDbContextFactory<PeachEbillsContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<int> GuardarAsync(
        string ruc,
        ResultadoRetencion resultado,
        ProveedorSri proveedor,
        short ambiente,
        DatilEmisionResult datil,
        string usuario,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await UpsertPersonaAsync(db, ruc, proveedor, cancellationToken);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var twh = new TaxWithHoldings
            {
                NumberPech = resultado.NumeroRetencion,
                PostOrderPeach = resultado.PostOrderPeach,
                Secuencial = resultado.Secuencial,
                DateIssued = resultado.FechaEmision,
                Ambient = ambiente,
                IssueType = 1,
                TransType = 2,
                Fperiodo = resultado.PeriodoFiscal,
                Contact = proveedor.Identificacion,
                TransmitterEstablishment = resultado.EstablishmentId,
                TransmitterRuc = ruc,
                DatilId = datil.Id,
                ClaveAcceso = datil.ClaveAcceso,
                IsValid = true,
            };
            db.TaxWithHoldings.Add(twh);
            await db.SaveChangesAsync(cancellationToken);

            foreach (var item in resultado.Retencion!.Items)
            {
                db.Thdetails.Add(new Thdetails
                {
                    TaxWithHolding = twh.Thid,
                    Code = item.Codigo,
                    PercentCode = item.CodigoPorcentaje,
                    Percent = item.Porcentaje,
                    AmountInTaxes = item.BaseImponible,
                    RtaxValue = item.ValorRetenido,
                    PurchaseDate = item.FechaEmisionDocumentoSustento.DateTime,
                    PurchaseNumber = item.NumeroDocumentoSustento,
                    PurchaseCodDoc = item.TipoDocumentoSustento,
                });
            }

            db.DatilRequests.Add(new DatilRequests
            {
                IsTaxWithH = true,
                RefId = twh.Thid,
                PostOrder = resultado.PostOrderPeach,
                DateRequest = DateTime.Now,
                DatilRequest = datil.RawResponse,
                Ruc = ruc,
                User = usuario,
            });

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return twh.Thid;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task UpsertPersonaAsync(
        PeachEbillsContext db, string ruc, ProveedorSri p, CancellationToken cancellationToken)
    {
        var persona = await db.Persons
            .FirstOrDefaultAsync(x => x.PersonId == p.Identificacion && x.Ructransmitter == ruc, cancellationToken);

        if (persona is null)
        {
            db.Persons.Add(new Persons
            {
                Ructransmitter = ruc,
                PersonId = p.Identificacion,
                Name = p.RazonSocial,
                Type = p.TipoIdentificacion,
                Email = p.Email,
                Phone = p.Telefono,
                Address = p.Direccion,
            });
        }
        else
        {
            if (persona.Name != p.RazonSocial) persona.Name = p.RazonSocial;
            if (persona.Address != p.Direccion) persona.Address = p.Direccion;
            if (persona.Phone != p.Telefono) persona.Phone = p.Telefono;
            if (persona.Email != p.Email) persona.Email = p.Email;
            if (persona.Type != p.TipoIdentificacion) persona.Type = p.TipoIdentificacion;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
