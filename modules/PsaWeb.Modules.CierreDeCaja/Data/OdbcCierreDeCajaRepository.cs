using System.Data;
using System.Data.Odbc;
using PsaWeb.Sage50;

namespace PsaWeb.Modules.CierreDeCaja.Data;

/// <summary>
/// Implementación real contra la base de Sage 50 (Pervasive / Actian Zen vía ODBC).
/// Reproduce las dos consultas del ejecutable original <c>ReceiptsReportRollerD</c>,
/// ahora con parámetros (<see cref="OdbcParameter"/>) en vez de interpolar fechas.
/// </summary>
internal sealed class OdbcCierreDeCajaRepository : ICierreDeCajaRepository
{
    // Cobros: suma por número de recibo. Journal de recibos de cliente.
    private const string SqlCobros = """
        SELECT Receipt.ReceiptNum AS Tipo, SUM(ABS(JrnlRow.Amount)) AS Subtotal
        FROM JrnlHdr Receipt, JrnlRow
        WHERE Receipt.PostOrder = JrnlRow.PostOrder
          AND Receipt.TransactionDate BETWEEN ? AND ?
          AND Receipt.JournalEx = 3
          AND Receipt.JrnlKey_Journal = 1
          AND JrnlRow.LinkToAnotherTrx > 0
        GROUP BY Receipt.ReceiptNum
        """;

    // Ventas: total facturado en el mismo rango. Journal de facturas de venta.
    private const string SqlVentas = """
        SELECT SUM(MainAmount) AS VentaTotal
        FROM JrnlHdr
        WHERE JrnlKey_Journal = 3
          AND JournalEx = 8
          AND JrnlTypeEx = 0
          AND TransactionDate BETWEEN ? AND ?
        """;

    private readonly ISageConnectionFactory _connections;

    public OdbcCierreDeCajaRepository(ISageConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<ResultadoCierre> ObtenerAsync(DateOnly desde, DateOnly hasta, CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var cobros = await LeerCobrosAsync(connection, desde, hasta, cancellationToken);
        var totalCobros = cobros.Sum(c => c.Subtotal);
        var totalVentas = await LeerTotalVentasAsync(connection, desde, hasta, cancellationToken);

        return new ResultadoCierre(cobros, totalCobros, totalVentas);
    }

    private static async Task<List<CobroPorTipo>> LeerCobrosAsync(
        OdbcConnection connection, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken)
    {
        await using var command = new OdbcCommand(SqlCobros, connection);
        AgregarRangoFechas(command, desde, hasta);

        var filas = new List<CobroPorTipo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var tipo = reader.IsDBNull(0) ? string.Empty : (reader.GetValue(0)?.ToString() ?? string.Empty);
            var subtotal = reader.IsDBNull(1) ? 0m : Convert.ToDecimal(reader.GetValue(1));
            filas.Add(new CobroPorTipo(tipo.Trim(), subtotal));
        }
        return filas;
    }

    private static async Task<decimal> LeerTotalVentasAsync(
        OdbcConnection connection, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken)
    {
        await using var command = new OdbcCommand(SqlVentas, connection);
        AgregarRangoFechas(command, desde, hasta);

        var escalar = await command.ExecuteScalarAsync(cancellationToken);
        return escalar is null or DBNull ? 0m : Convert.ToDecimal(escalar);
    }

    private static void AgregarRangoFechas(OdbcCommand command, DateOnly desde, DateOnly hasta)
    {
        // ODBC usa parámetros posicionales: el orden debe coincidir con los '?' del SQL.
        command.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = desde.ToDateTime(TimeOnly.MinValue) });
        command.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = hasta.ToDateTime(TimeOnly.MinValue) });
    }
}
