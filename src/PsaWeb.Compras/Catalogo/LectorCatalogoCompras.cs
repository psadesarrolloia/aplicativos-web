using System.Data.Common;
using System.Data.Odbc;

namespace PsaWeb.Compras.Catalogo;

/// <summary>Catálogos que vienen de PeachEBills (los lee quien tenga el contexto EF; acá solo se combinan).</summary>
public sealed record CatalogoPeachEbills(
    IReadOnlyList<FormaPagoRetencion> FormasPago,
    IReadOnlyList<TipoPagoSri> TiposPagoSri,
    IReadOnlyList<TarifaIva> TarifasIva,
    IReadOnlyDictionary<string, string> TiposImpuesto);

/// <summary>
/// Lee de Sage (ODBC, solo SELECT) los ítems y la primera cuenta, y arma <see cref="CatalogoCompras"/> con los mismos
/// filtros que <c>Bill.LoadingInfoForItems</c> / <c>sageItems</c> del `.exe`.
/// </summary>
public static class LectorCatalogoCompras
{
    // ItemClass de LineItem = InventoryItemClassification del SDK - 1.
    internal const int ClaseNoStock = 0;
    internal const int ClaseStock = 1;
    internal const int ClaseStockSerializado = 10;

    private const string SqlItems = """
        SELECT l.ItemID, l.ItemDescription, l.ItemClass, l.Category, l.ItemIsInactive,
               l.CustomField1, l.CustomField2, l.CustomField3, l.CustomField4, l.CustomField5,
               c.AccountID AS CuentaInventario
        FROM LineItem l LEFT OUTER JOIN Chart c ON l.InvAcctRecordNumber = c.GLAcntNumber
        ORDER BY l.ItemID
        """;

    // El SDK lista las cuentas por ID; la línea AUT-SRI usa la primera (F0-b: 10000 en CPTDC).
    private const string SqlPrimeraCuenta = "SELECT TOP 1 AccountID FROM Chart ORDER BY AccountID";

    public static async Task<CatalogoCompras> LeerAsync(
        OdbcConnection conexion, CatalogoPeachEbills peachEbills, CancellationToken cancellationToken = default)
    {
        var items = new List<ItemSage>();
        await using (var cmd = new OdbcCommand(SqlItems, conexion))
        await using (var r = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await r.ReadAsync(cancellationToken))
            {
                items.Add(new ItemSage(
                    Id: Texto(r, "ItemID"),
                    Descripcion: Texto(r, "ItemDescription"),
                    Clase: Convert.ToInt32(r["ItemClass"]),
                    Categoria: Texto(r, "Category"),
                    CustomField1: Texto(r, "CustomField1"),
                    CustomField2: Texto(r, "CustomField2"),
                    CustomField3: Texto(r, "CustomField3"),
                    CustomField4: Texto(r, "CustomField4"),
                    CustomField5: Texto(r, "CustomField5"),
                    CuentaInventario: r.IsDBNull(r.GetOrdinal("CuentaInventario")) ? null : Texto(r, "CuentaInventario"),
                    Inactivo: Convert.ToInt32(r["ItemIsInactive"]) != 0));
            }
        }

        string primeraCuenta;
        await using (var cmd = new OdbcCommand(SqlPrimeraCuenta, conexion))
        {
            primeraCuenta = Convert.ToString(await cmd.ExecuteScalarAsync(cancellationToken))?.Trim() ?? string.Empty;
        }

        return Armar(items, primeraCuenta, peachEbills);
    }

    /// <summary>Aplica los filtros del `.exe` sobre la lista completa de ítems (en el orden de <c>ORDER BY ItemID</c>).</summary>
    public static CatalogoCompras Armar(IReadOnlyList<ItemSage> items, string primeraCuenta, CatalogoPeachEbills pe)
    {
        string[] especiales = ["IMPUESTO", "COMPRA", "R-IRF", "R-IVA"];
        return new CatalogoCompras
        {
            ItemsC = items.Where(x => x.CustomField1 == "COMPRA").ToList(),
            ItemsImpuesto = items.Where(x => !x.Inactivo && x.Categoria == "IMPUESTO").ToList(),
            ItemsRetencionFuente = items.Where(x => !x.Inactivo && x.Categoria == "R-IRF").ToList(),
            ItemsRetencionIva = items.Where(x => !x.Inactivo && x.Categoria == "R-IVA").ToList(),
            // sageItems con clasificaciones reemplaza el filtro de inactivos: el `.exe` ofrece también los inactivos.
            ItemsDetalle = items.Where(x => x.Clase is ClaseNoStock or ClaseStock or ClaseStockSerializado
                                            && !especiales.Contains(x.Categoria) && x.Id != "AUT-SRI").ToList(),
            ItemAutorizacion = items.FirstOrDefault(x => !x.Inactivo
                && (x.Id.Equals("AUT-SRI", StringComparison.OrdinalIgnoreCase) || x.Id.Equals("AUTSRI", StringComparison.OrdinalIgnoreCase))),
            PrimeraCuenta = primeraCuenta,
            FormasPago = pe.FormasPago,
            TiposPagoSri = pe.TiposPagoSri,
            TarifasIva = pe.TarifasIva,
            TiposImpuesto = pe.TiposImpuesto,
        };
    }

    private const string SqlProveedores = """
        SELECT v.VendorID, v.VendorRecordNumber, v.OurAccountWithThem, v.Name, v.CustomField0, v.CustomField1,
               v.CustomField3, v.CustomField4, v.Email, v.PhoneNumber, v.PhoneNumber2, v.IsInactive,
               a.AddressLine1, a.AddressLine2, a.Country, c.AccountID AS CuentaGasto
        FROM Vendors v, Address a, Chart c
        WHERE v.VendorRecordNumber = a.VendorRecordNumber AND v.GLAcntNumber = c.GLAcntNumber
        """;

    /// <summary>Proveedores cuya identificación (<c>Address.Country</c>) es <paramref name="identificacion"/> (port de <c>sageVendors</c>).</summary>
    public static Task<IReadOnlyList<ProveedorSage>> ProveedoresPorIdentificacionAsync(
        OdbcConnection conexion, string identificacion, CancellationToken cancellationToken = default)
        => LeerProveedoresAsync(conexion, " AND a.Country = ?", identificacion, cancellationToken);

    public static async Task<ProveedorSage?> ProveedorPorIdAsync(
        OdbcConnection conexion, string vendorId, CancellationToken cancellationToken = default)
        => (await LeerProveedoresAsync(conexion, " AND v.VendorID = ?", vendorId, cancellationToken)).FirstOrDefault();

    /// <summary>IDs de todos los proveedores (para validar/proponer el ID de uno nuevo).</summary>
    public static async Task<IReadOnlyList<string>> IdsProveedoresAsync(OdbcConnection conexion, CancellationToken cancellationToken = default)
    {
        var ids = new List<string>();
        await using var cmd = new OdbcCommand("SELECT VendorID FROM Vendors", conexion);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken)) ids.Add(Texto(r, "VendorID"));
        return ids;
    }

    private static async Task<IReadOnlyList<ProveedorSage>> LeerProveedoresAsync(
        OdbcConnection conexion, string filtro, string valor, CancellationToken cancellationToken)
    {
        var lista = new List<ProveedorSage>();
        await using var cmd = new OdbcCommand(SqlProveedores + filtro, conexion);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = valor });
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            lista.Add(new ProveedorSage(
                Id: Texto(r, "VendorID"),
                RecordNumber: Texto(r, "VendorRecordNumber"),
                TipoIdentificacion: Texto(r, "OurAccountWithThem"),
                Nombre: Texto(r, "Name"),
                CustomField0: Texto(r, "CustomField0"),
                CustomField1: Texto(r, "CustomField1"),
                CustomField3: Texto(r, "CustomField3"),
                CustomField4: Texto(r, "CustomField4"),
                Direccion1: Texto(r, "AddressLine1"),
                Direccion2: Texto(r, "AddressLine2"),
                Pais: Texto(r, "Country"),
                Email: Texto(r, "Email"),
                Telefono: Texto(r, "PhoneNumber"),
                Telefono2: Texto(r, "PhoneNumber2"),
                CuentaGasto: Texto(r, "CuentaGasto"),
                Inactivo: Convert.ToInt32(r["IsInactive"]) != 0));
        }
        return lista;
    }

    private static string Texto(DbDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? string.Empty : (r.GetValue(i)?.ToString() ?? string.Empty).Trim();
    }
}
