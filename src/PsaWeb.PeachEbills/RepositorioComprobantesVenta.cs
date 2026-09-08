using Microsoft.EntityFrameworkCore;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.PeachEbills;

/// <summary>
/// Persiste en PeachEBills un comprobante de venta ya emitido en Datil (factura,
/// liquidación o nota de crédito): upsert de <c>Persons</c> y, en una
/// transacción, alta de <c>Facturas</c> + <c>Details</c> (+ <c>Payments</c> para
/// facturas) (+ <c>NCdetail</c> para notas de crédito) (+ <c>FacturaPropiedadExterna</c>
/// con el fax) + <c>DatilRequests</c>. Port de <c>CRUDsql.BillIdCreate</c> /
/// <c>NCIdCreate</c>. Sólo se llama tras una emisión real (no en <c>DryRun</c>).
/// </summary>
public sealed class RepositorioComprobantesVenta
{
    private readonly IDbContextFactory<PeachEbillsContext> _contextFactory;

    public RepositorioComprobantesVenta(IDbContextFactory<PeachEbillsContext> contextFactory)
        => _contextFactory = contextFactory;

    /// <summary>Guarda una factura de venta o una liquidación de compra. Devuelve el <c>FacturaId</c>.</summary>
    public Task<int> GuardarFacturaAsync(
        string ruc,
        Facturas factura,
        Persons persona,
        IReadOnlyList<Details> detalles,
        string datilRawResponse,
        string usuario,
        CancellationToken cancellationToken = default)
        => EjecutarAsync(ruc, factura, persona, detalles, ncDetail: null, datilRawResponse, usuario, cancellationToken);

    /// <summary>Guarda una nota de crédito de venta (factura + fila <c>NCdetail</c>). Devuelve el <c>FacturaId</c>.</summary>
    public Task<int> GuardarNotaCreditoAsync(
        string ruc,
        Facturas nota,
        Persons persona,
        IReadOnlyList<Details> detalles,
        NcDetail detalleNc,
        string datilRawResponse,
        string usuario,
        CancellationToken cancellationToken = default)
        => EjecutarAsync(ruc, nota, persona, detalles, detalleNc, datilRawResponse, usuario, cancellationToken);

    private async Task<int> EjecutarAsync(
        string ruc,
        Facturas factura,
        Persons persona,
        IReadOnlyList<Details> detalles,
        NcDetail? ncDetail,
        string datilRawResponse,
        string usuario,
        CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await UpsertPersonaAsync(db, ruc, persona, cancellationToken);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            factura.Comprador = persona.PersonId;
            db.Facturas.Add(factura);
            await db.SaveChangesAsync(cancellationToken); // asigna FacturaId

            if (EsFactura(factura.CodDoc))
            {
                var tipoPago = await db.PaymentTypes
                    .OrderBy(p => p.PaymentTypeId)
                    .FirstOrDefaultAsync(cancellationToken);
                if (tipoPago is not null)
                {
                    db.Payments.Add(new Payments
                    {
                        PaymentDate = factura.DateIssued,
                        PaymentType = tipoPago.PaymentTypeId,
                        PaymentTotal = factura.TotalAmount,
                        Factura = factura.FacturaId,
                    });
                }
            }

            foreach (var detalle in detalles)
            {
                detalle.Factura = factura.FacturaId;
                db.Details.Add(detalle);
            }

            if (ncDetail is not null)
            {
                ncDetail.NCid = factura.FacturaId;
                db.NcDetails.Add(ncDetail);
            }

            var fax = persona.FaxNum?.Trim();
            if (!string.IsNullOrEmpty(fax) && fax.Length > 1)
            {
                db.FacturaPropiedadExterna.Add(new FacturaPropiedadExterna
                {
                    Factura = factura.FacturaId,
                    Name = "FAX_Number",
                    ValueData = fax,
                });
            }

            db.DatilRequests.Add(new DatilRequests
            {
                IsTaxWithH = false,
                RefId = factura.FacturaId,
                PostOrder = Recortar(factura.PostOrderPeach, 50),
                DateRequest = DateTime.Now,
                DatilRequest = datilRawResponse,
                Ruc = ruc,
                User = usuario,
            });

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return factura.FacturaId;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>codDoc "01" = factura (y liquidación "03" NO lleva pago acá, igual que el .exe).</summary>
    private static bool EsFactura(string codDoc)
    {
        var c = codDoc.Trim();
        return c is "01" or "1";
    }

    private static string Recortar(string? valor, int max)
    {
        valor ??= string.Empty;
        return valor.Length <= max ? valor : valor[..max];
    }

    private static async Task UpsertPersonaAsync(
        PeachEbillsContext db, string ruc, Persons persona, CancellationToken cancellationToken)
    {
        var existente = await db.Persons
            .FirstOrDefaultAsync(x => x.PersonId == persona.PersonId && x.Ructransmitter == ruc, cancellationToken);

        if (existente is null)
        {
            persona.Ructransmitter = ruc;
            db.Persons.Add(persona);
        }
        else
        {
            if (existente.Name != persona.Name) existente.Name = persona.Name;
            if (existente.Address != persona.Address) existente.Address = persona.Address;
            if (existente.Phone != persona.Phone) existente.Phone = persona.Phone;
            if (existente.Email != persona.Email) existente.Email = persona.Email;
            if (existente.Type != persona.Type) existente.Type = persona.Type;
            if (existente.FaxNum != persona.FaxNum) existente.FaxNum = persona.FaxNum;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
