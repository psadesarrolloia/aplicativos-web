using System.Data.Odbc;
using System.Globalization;
using System.Text;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.Ventas.Data;

/// <summary>
/// Lecturas reales contra Sage 50 (Pervasive vía ODBC), solo <c>SELECT</c> con parámetros posicionales. La empresa la resuelve
/// <see cref="IResolverEmpresaSage"/> (RUC de sesión). Sin Bridge ni cola: las existencias son en tiempo real.
/// <list type="bullet">
///   <item>Ítems: <c>LineItem</c> (<c>ItemClass 1</c> = stock; <c>3</c> = ensamblados, que en SANCEV son los tableros «TE» hechos por proyecto).</item>
///   <item>Existencia: Σ <c>InventoryCosts.Quantity</c> con <c>MajorType</c> 1 y 2 (compras y ventas negativas) — idéntica a <c>InventoryItem.QuantityOnHand</c> del SDK (40/40 ítems, 2026-10-01).</item>
///   <item>Precios: <c>LineItem.PriceLevel1Amount … 10</c>; el nivel de un cliente es <c>Customers.PriceLevel + 1</c> (<see cref="PreciosDeVenta"/>).</item>
/// </list>
/// </summary>
internal sealed class OdbcVentasRepository : IVentasRepository
{
    private const int LoteIn = 200;
    private const int CandidatosSinFiltroDeExistencia = 2000;
    private const int MaximoDeTerminos = 5;

    private const string ColumnasPrecio =
        "PriceLevel1Amount, PriceLevel2Amount, PriceLevel3Amount, PriceLevel4Amount, PriceLevel5Amount, " +
        "PriceLevel6Amount, PriceLevel7Amount, PriceLevel8Amount, PriceLevel9Amount, PriceLevel10Amount";

    private readonly ISageConnectionFactory _connections;
    private readonly IResolverEmpresaSage _resolver;

    public OdbcVentasRepository(ISageConnectionFactory connections, IResolverEmpresaSage resolver)
    {
        _connections = connections;
        _resolver = resolver;
    }

    public async Task<IReadOnlyList<string>> CategoriasAsync(bool incluirEnsamblados, CancellationToken cancellationToken = default)
    {
        await using var cn = await AbrirAsync(cancellationToken);
        var sql = $"SELECT DISTINCT Category FROM LineItem WHERE ItemIsInactive = 0 AND ItemClass IN ({Clases(incluirEnsamblados)}) ORDER BY Category";
        var lista = new List<string>();
        await using var cmd = new OdbcCommand(sql, cn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var c = Texto(reader, 0);
            if (c.Length > 0) lista.Add(c);
        }
        return lista;
    }

    public async Task<IReadOnlyList<ItemVenta>> BuscarItemsAsync(FiltroItems filtro, CancellationToken cancellationToken = default)
    {
        await using var cn = await AbrirAsync(cancellationToken);
        var limite = filtro.SoloConExistencia ? CandidatosSinFiltroDeExistencia : Math.Clamp(filtro.Maximo, 1, 500);

        var sql = new StringBuilder($"SELECT TOP {limite} ItemRecordNumber, ItemID, ItemDescription, Category, StockingUM, ItemClass, {ColumnasPrecio} FROM LineItem ");
        sql.Append($"WHERE ItemIsInactive = 0 AND ItemClass IN ({Clases(filtro.IncluirEnsamblados)}) ");
        var parametros = new List<string>();
        foreach (var t in Terminos(filtro.Texto))
        {
            sql.Append("AND (UPPER(ItemID) LIKE ? OR UPPER(ItemDescription) LIKE ? OR UPPER(PartNumber) LIKE ? OR UPPER(UPC_SKU) LIKE ?) ");
            var patron = "%" + t + "%";
            parametros.AddRange(new[] { patron, patron, patron, patron });
        }
        if (!string.IsNullOrWhiteSpace(filtro.Categoria))
        {
            sql.Append("AND Category = ? ");
            parametros.Add(filtro.Categoria.Trim());
        }
        sql.Append("ORDER BY ItemID");

        var items = new List<(int Registro, ItemVenta Item)>();
        await using (var cmd = new OdbcCommand(sql.ToString(), cn))
        {
            foreach (var p in parametros) cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = p });
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var precios = new decimal[PreciosDeVenta.NivelesMaximos];
                for (var i = 0; i < precios.Length; i++) precios[i] = Dec(reader, 6 + i);
                items.Add((Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
                    new ItemVenta(Texto(reader, 1), Texto(reader, 2), Texto(reader, 3), Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture) == 3,
                        Texto(reader, 4), 0m, precios)));
            }
        }

        var existencias = await ExistenciasAsync(cn, items.Select(x => x.Registro).ToList(), cancellationToken);
        var resultado = items.Select(x => x.Item with { Existencia = existencias.GetValueOrDefault(x.Registro) });
        if (filtro.SoloConExistencia) resultado = resultado.Where(i => i.Existencia > 0);
        return resultado.Take(Math.Clamp(filtro.Maximo, 1, 500)).ToList();
    }

    public async Task<IReadOnlyList<ClienteVenta>> BuscarClientesAsync(string texto, int maximo = 20, CancellationToken cancellationToken = default)
    {
        await using var cn = await AbrirAsync(cancellationToken);

        var vendedores = new Dictionary<int, string>();
        await using (var cmdV = new OdbcCommand("SELECT EmpRecordNumber, EmployeeID FROM Employee", cn))
        await using (var rv = await cmdV.ExecuteReaderAsync(cancellationToken))
        {
            while (await rv.ReadAsync(cancellationToken))
            {
                vendedores[Convert.ToInt32(rv.GetValue(0), CultureInfo.InvariantCulture)] = Texto(rv, 1);
            }
        }

        var sql = new StringBuilder($"SELECT TOP {Math.Clamp(maximo, 1, 100)} CustomerID, Customer_Bill_Name, Contact, Phone_Number, eMail_Address, " +
                                    "PriceLevel, Terms_DueDays, Terms_CreditLimit, Balance, EmpRecordNumber FROM Customers WHERE CustomerIsInactive = 0 ");
        var parametros = new List<string>();
        foreach (var t in Terminos(texto))
        {
            sql.Append("AND (UPPER(CustomerID) LIKE ? OR UPPER(Customer_Bill_Name) LIKE ?) ");
            var patron = "%" + t + "%";
            parametros.AddRange(new[] { patron, patron });
        }
        sql.Append("ORDER BY CustomerID");

        var lista = new List<ClienteVenta>();
        await using var cmd = new OdbcCommand(sql.ToString(), cn);
        foreach (var p in parametros) cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = p });
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var rep = Convert.ToInt32(reader.GetValue(9), CultureInfo.InvariantCulture);
            var id = Texto(reader, 0);
            var nombre = Texto(reader, 1);
            lista.Add(new ClienteVenta(id, nombre.Length > 0 ? nombre : id, Texto(reader, 2), Texto(reader, 3), Texto(reader, 4),
                Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture), Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture),
                Dec(reader, 7), Dec(reader, 8), vendedores.GetValueOrDefault(rep)));
        }
        return lista;
    }

    // --- interno -------------------------------------------------------------

    private static async Task<Dictionary<int, decimal>> ExistenciasAsync(OdbcConnection cn, IReadOnlyList<int> registros, CancellationToken ct)
    {
        var resultado = new Dictionary<int, decimal>();
        for (var i = 0; i < registros.Count; i += LoteIn)
        {
            var lote = registros.Skip(i).Take(LoteIn).ToList();
            var marcas = string.Join(", ", lote.Select(_ => "?"));
            await using var cmd = new OdbcCommand(
                $"SELECT ItemRecNumber, SUM(Quantity) FROM InventoryCosts WHERE MajorType IN (1, 2) AND ItemRecNumber IN ({marcas}) GROUP BY ItemRecNumber", cn);
            foreach (var r in lote) cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = r });
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                resultado[Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture)] = Dec(reader, 1);
            }
        }
        return resultado;
    }

    private async Task<OdbcConnection> AbrirAsync(CancellationToken ct)
    {
        var cadena = _resolver.RucSesion is { } ruc ? await _resolver.CadenaOdbcAsync(ruc, ct) : null;
        var cn = cadena is null ? _connections.CreateConnection() : _connections.CreateConnection(cadena);
        await cn.OpenAsync(ct);
        return cn;
    }

    private static string Clases(bool incluirEnsamblados) => incluirEnsamblados ? "1, 3" : "1";

    /// <summary>Palabras del texto de búsqueda en mayúsculas (cada una debe aparecer), sin comodines propios del usuario.</summary>
    internal static IReadOnlyList<string> Terminos(string? texto) =>
        string.IsNullOrWhiteSpace(texto)
            ? Array.Empty<string>()
            : texto.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.Replace("%", string.Empty).Replace("_", string.Empty))
                .Where(t => t.Length > 0).Take(MaximoDeTerminos).ToList();

    private static string Texto(System.Data.Common.DbDataReader r, int i) => r.IsDBNull(i) ? string.Empty : (r.GetValue(i)?.ToString() ?? string.Empty).Trim();

    private static decimal Dec(System.Data.Common.DbDataReader r, int i) =>
        r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i), CultureInfo.InvariantCulture);
}
