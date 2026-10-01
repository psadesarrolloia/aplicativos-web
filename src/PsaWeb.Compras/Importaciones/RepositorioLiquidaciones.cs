using Microsoft.EntityFrameworkCore;
using PsaWeb.PeachEbills.Data;

namespace PsaWeb.Compras.Importaciones;

/// <summary>Liquidación guardada en PeachEBills (el último <c>ImportCost</c> de la cuenta, con sus ítems y gastos en orden de id).</summary>
public sealed record LiquidacionGuardada(
    int Id,
    string? ProveedorId,
    int? PostOrder,
    string? ClaveOc,
    string? ReferenciaOc,
    DateTime? Fecha,
    List<ItemLiquidacion> Items,
    List<GastoImportacion> Gastos);

/// <summary>Lo que se guarda al pulsar «Guardar» o al anotar la OC.</summary>
public sealed record DatosLiquidacion(
    string ProveedorId,
    string ReferenciaOc,
    DateTime Fecha,
    IReadOnlyList<ItemLiquidacion> Items,
    IReadOnlyList<GastoImportacion> Gastos);

/// <summary>Lectura y guardado de <c>ImportCost</c>/<c>ImportCostApportion</c>/<c>ImportCostEx</c>.</summary>
public static class RepositorioLiquidaciones
{
    /// <summary>La última liquidación guardada de la cuenta (el `.exe` leía la de mayor id).</summary>
    public static async Task<LiquidacionGuardada?> UltimaAsync(PeachEbillsContext db, string ruc, string cuenta, CancellationToken ct = default)
    {
        var c = await db.ImportCost.AsNoTracking()
            .Where(x => x.TransmitterRuc == ruc && x.AccountId == cuenta)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (c is null) return null;
        var items = await db.ImportCostApportion.AsNoTracking().Where(x => x.ImportCostId == c.Id).OrderBy(x => x.Id).ToListAsync(ct);
        var gastos = await db.ImportCostEx.AsNoTracking().Where(x => x.ImportCostId == c.Id).OrderBy(x => x.Id).ToListAsync(ct);
        return new LiquidacionGuardada(
            c.Id,
            c.VendorId?.Trim(),
            int.TryParse(c.PostOrderId, out var po) ? po : null,
            string.IsNullOrWhiteSpace(c.PostOrderStrKey) ? null : c.PostOrderStrKey.Trim(),
            c.PostOrderNumber?.Trim(),
            c.TransactionDate,
            items.Select(x => new ItemLiquidacion
            {
                ItemId = x.ItemId.Trim(),
                Descripcion = x.Description,
                Cantidad = x.Quantity,
                Valor = x.ImportValue,
                Porcentaje = x.ApportionPercent,
                Prorrateo = x.Apportion,
            }).ToList(),
            gastos.Select(x => new GastoImportacion
            {
                Fecha = x.Date,
                Proveedor = x.VendorName,
                Referencia = x.Reference,
                Descripcion = x.Description,
                Valor = x.ExpenseValue,
                EsGasto = x.IsCost,
            }).ToList());
    }

    /// <summary>Referencia de la OC de la última liquidación de la empresa (para proponer la siguiente).</summary>
    public static async Task<string?> UltimaReferenciaAsync(PeachEbillsContext db, string ruc, CancellationToken ct = default) =>
        (await db.ImportCost.AsNoTracking()
            .Where(x => x.TransmitterRuc == ruc && x.PostOrderNumber != null && x.PostOrderNumber != "" && x.PostOrderId != null && x.PostOrderId != "")
            .OrderByDescending(x => x.TransactionDate).ThenByDescending(x => x.Id)
            .Select(x => x.PostOrderNumber).FirstOrDefaultAsync(ct))?.Trim();

    /// <summary>Resumen de lo guardado por cuenta (para la lista): proveedor, PostOrder y referencia de la OC.</summary>
    public static async Task<Dictionary<string, (string? Proveedor, int? PostOrder, string? Referencia)>> ResumenAsync(
        PeachEbillsContext db, string ruc, CancellationToken ct = default)
    {
        var filas = await db.ImportCost.AsNoTracking().Where(x => x.TransmitterRuc == ruc)
            .Select(x => new { x.Id, x.AccountId, x.VendorId, x.PostOrderId, x.PostOrderNumber }).ToListAsync(ct);
        return filas.GroupBy(x => x.AccountId.Trim())
            .Select(g => g.OrderByDescending(x => x.Id).First())
            .ToDictionary(x => x.AccountId.Trim(),
                x => (x.VendorId?.Trim(), int.TryParse(x.PostOrderId, out var po) ? po : (int?)null, x.PostOrderNumber?.Trim()));
    }

    /// <summary>
    /// «Guardar» (C3): actualiza en el lugar la última liquidación de la cuenta (cabecera, y reemplaza ítems y gastos) o la crea. El
    /// vínculo con la OC (PostOrder, GUID) se conserva: solo lo cambia <see cref="AnotarOcAsync"/>. Devuelve el id.
    /// </summary>
    public static async Task<int> GuardarAsync(PeachEbillsContext db, string ruc, string cuenta, DatosLiquidacion d, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var c = await db.ImportCost.Where(x => x.TransmitterRuc == ruc && x.AccountId == cuenta).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (c is null)
        {
            c = new ImportCost { AccountId = cuenta, TransmitterRuc = ruc, PostOrderId = string.Empty, PostOrderStrKey = string.Empty };
            db.ImportCost.Add(c);
        }
        c.VendorId = string.IsNullOrWhiteSpace(d.ProveedorId) ? null : d.ProveedorId.Trim();
        c.PostOrderNumber = d.ReferenciaOc;
        c.TransactionDate = d.Fecha;
        await db.SaveChangesAsync(ct);

        db.ImportCostApportion.RemoveRange(db.ImportCostApportion.Where(x => x.ImportCostId == c.Id));
        db.ImportCostEx.RemoveRange(db.ImportCostEx.Where(x => x.ImportCostId == c.Id));
        foreach (var x in d.Items)
        {
            db.ImportCostApportion.Add(new ImportCostApportion
            {
                ImportCostId = c.Id,
                ItemId = x.ItemId.Trim(),
                Description = Cortar(x.Descripcion, 150),
                Quantity = x.Cantidad,
                ImportValue = x.Valor,
                ApportionPercent = x.Porcentaje,
                Apportion = x.Prorrateo,
            });
        }
        foreach (var x in d.Gastos)
        {
            db.ImportCostEx.Add(new ImportCostEx
            {
                ImportCostId = c.Id,
                Date = x.Fecha,
                VendorName = Cortar(x.Proveedor, 90),
                Reference = Cortar(x.Referencia, 50),
                Description = Cortar(x.Descripcion, 160),
                ExpenseValue = x.Valor,
                IsCost = x.EsGasto,
            });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return c.Id;
    }

    /// <summary>Anota (o borra, con <paramref name="postOrder"/> nulo) la OC de la liquidación, como el evento <c>Saved</c> del `.exe`.</summary>
    public static async Task AnotarOcAsync(PeachEbillsContext db, int id, int? postOrder, string? clave, string? referencia, DateTime? fecha, CancellationToken ct = default)
    {
        var c = await db.ImportCost.FirstAsync(x => x.Id == id, ct);
        c.PostOrderId = postOrder?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        c.PostOrderStrKey = clave ?? string.Empty;
        if (referencia is not null) c.PostOrderNumber = referencia;
        if (fecha is not null) c.TransactionDate = fecha;
        await db.SaveChangesAsync(ct);
    }

    private static string Cortar(string? s, int max) => s is null ? string.Empty : s.Length <= max ? s : s[..max];
}
