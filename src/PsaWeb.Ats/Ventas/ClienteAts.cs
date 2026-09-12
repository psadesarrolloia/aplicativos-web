using System.Data.Common;
using System.Data.Odbc;

namespace PsaWeb.Ats.Ventas;

/// <summary>Cliente de ventas leído de Sage 50 para una fila del ATS.</summary>
/// <param name="TipoIdentificacion">
/// Uno de <see cref="TiposIdentificacionClienteAts"/>, o <c>null</c> si Sage no
/// tiene ninguna dirección para el cliente (el `.exe` deja el campo del XML sin
/// emitir en ese caso — ver <see cref="LectorClienteAts"/>).
/// </param>
/// <param name="Identificacion">Cédula/RUC/pasaporte ya resuelto.</param>
/// <param name="TipoCliente"><c>Customers.AccountNumber</c> — solo se usa en el
/// esquema cuando el cliente es del exterior.</param>
/// <param name="Nombre"><c>Customer_Bill_Name</c> + continuación — solo se usa
/// en el esquema cuando el cliente es del exterior.</param>
public sealed record ClienteAts(string? TipoIdentificacion, string Identificacion, string TipoCliente, string Nombre)
{
    public static readonly ClienteAts Vacio = new(null, string.Empty, string.Empty, string.Empty);
}

/// <summary>
/// Lee <c>Customers</c> + <c>Address</c> por <c>CustomerRecordNumber</c>. Port
/// de <c>LoadCustomer</c> (<c>ATSfromPeach</c>).
/// </summary>
public static class LectorClienteAts
{
    // Sin filtro de Address.AddressTypeNumber (a diferencia de LectorProveedor):
    // el `.exe` tampoco lo tiene. Si un cliente tiene más de una dirección, gana
    // la última fila que devuelva el driver (mismo comportamiento del `.exe`,
    // no determinístico en ese caso raro).
    private const string Sql = """
        SELECT Customers.Customer_Bill_Name AS Nombre,
               Customers.CustomField4       AS Ruc,
               Customers.CustomField5       AS Pasaporte,
               Customers.CustomField1       AS ContinuacionNombre,
               Address.Country              AS RucAlterno,
               Customers.AccountNumber      AS TipoCliente
        FROM Customers, Address
        WHERE Customers.CustomerRecordNumber = Address.CustomerRecordNumber
          AND NOT (Customers.Customer_Bill_Name LIKE 'ANULAD%')
          AND Customers.CustomerRecordNumber = ?
        """;

    public static async Task<ClienteAts> LeerAsync(
        OdbcConnection connection, long customerId, CancellationToken cancellationToken = default)
    {
        var nombre = string.Empty;
        var tipoCliente = string.Empty;
        var identificacion = string.Empty;
        string? tipoIdentificacion = null;

        await using var cmd = new OdbcCommand(Sql, connection);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = customerId });
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await r.ReadAsync(cancellationToken))
        {
            var continuacion = NullSiVacio(r, "ContinuacionNombre") ?? string.Empty;
            var nombreFila = NullSiVacio(r, "Nombre");
            if (nombreFila is not null)
            {
                nombre = nombreFila + continuacion;
            }

            var tipoClienteFila = NullSiVacio(r, "TipoCliente");
            if (tipoClienteFila is not null)
            {
                tipoCliente = tipoClienteFila;
            }

            var pasaporte = NullSiVacio(r, "Pasaporte");
            if (pasaporte is not null)
            {
                identificacion = pasaporte;
                if (identificacion.Length == 0)
                {
                    identificacion = NullSiVacio(r, "Ruc") ?? string.Empty;
                    if (identificacion.Length == 0)
                    {
                        identificacion = NullSiVacio(r, "RucAlterno") ?? string.Empty;
                    }
                }
                else
                {
                    // Sin resguardo de "ya asignado": una fila posterior con
                    // pasaporte no vacío pisa el tipo igual, tal cual el `.exe`.
                    tipoIdentificacion = TiposIdentificacionClienteAts.Exterior;
                }
            }
            else
            {
                var ruc = NullSiVacio(r, "Ruc");
                if (ruc is not null)
                {
                    identificacion = ruc;
                    if (identificacion.Length == 0)
                    {
                        identificacion = NullSiVacio(r, "RucAlterno") ?? string.Empty;
                    }
                }
            }

            if (identificacion.Length > 0 && tipoIdentificacion is null)
            {
                tipoIdentificacion = TiposIdentificacionClienteAts.Deducir(identificacion);
            }
        }

        return new ClienteAts(tipoIdentificacion, identificacion, tipoCliente, nombre);
    }

    private static string? NullSiVacio(DbDataReader r, string columna)
    {
        var i = r.GetOrdinal(columna);
        return r.IsDBNull(i) ? null : r.GetValue(i)?.ToString();
    }
}
