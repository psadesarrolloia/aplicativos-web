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
/// <param name="TipoCliente"><c>Customers.AccountNumber</c> — solo se informa
/// en el esquema como <c>tipoCliente</c> cuando el cliente es del exterior.</param>
/// <param name="Nombre"><c>Customer_Bill_Name</c> + continuación — solo se usa
/// en el esquema cuando el cliente es del exterior.</param>
/// <param name="EsParteRelacionada">
/// Convención acordada con el usuario 2026-09-14: Sage 50 no tiene un campo
/// dedicado para "parte relacionada", así que se marca asignándole al cliente
/// un "Sales Rep" (<c>Customers.EmpRecordNumber</c> → <c>Employee</c>) cuyo
/// <c>EmployeeID</c> o <c>EmployeeName</c> sea la palabra "SI RELACIONADO"
/// (sin distinguir mayúsculas/espacios). No usa <see cref="TipoCliente"/> — ese
/// campo (<c>AccountNumber</c>) ya lo usa el `.exe` original para el
/// <c>tipoCliente</c> de clientes del exterior; reusarlo también para "parte
/// relacionada" pisaba ese dato (confirmado con el XML real de CPTDC
/// julio/2026: cliente 20550511941, tipoCliente="02" en el declarado).
/// </param>
public sealed record ClienteAts(
    string? TipoIdentificacion, string Identificacion, string TipoCliente, string Nombre, bool EsParteRelacionada)
{
    public static readonly ClienteAts Vacio = new(null, string.Empty, string.Empty, string.Empty, false);
}

/// <summary>
/// Lee <c>Customers</c> + <c>Address</c> (+ <c>Employee</c> por el "Sales Rep")
/// por <c>CustomerRecordNumber</c>. Port de <c>LoadCustomer</c>
/// (<c>ATSfromPeach</c>), extendido con la convención de "parte relacionada".
/// </summary>
public static class LectorClienteAts
{
    private const string SalesRepRelacionado = "SI RELACIONADO";

    // Sin filtro de Address.AddressTypeNumber (a diferencia de LectorProveedor):
    // el `.exe` tampoco lo tiene. Si un cliente tiene más de una dirección, gana
    // la última fila que devuelva el driver (mismo comportamiento del `.exe`,
    // no determinístico en ese caso raro). El join a Employee es LEFT OUTER:
    // la mayoría de los clientes no tiene Sales Rep asignado.
    private const string Sql = """
        SELECT Customers.Customer_Bill_Name AS Nombre,
               Customers.CustomField4       AS Ruc,
               Customers.CustomField5       AS Pasaporte,
               Customers.CustomField1       AS ContinuacionNombre,
               Address.Country              AS RucAlterno,
               Customers.AccountNumber      AS TipoCliente,
               Employee.EmployeeID          AS SalesRepId,
               Employee.EmployeeName        AS SalesRepNombre
        FROM { oj Customers LEFT OUTER JOIN Employee ON Customers.EmpRecordNumber = Employee.EmpRecordNumber }, Address
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
        var esParteRelacionada = false;
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

            if (EsSalesRepRelacionado(NullSiVacio(r, "SalesRepId")) || EsSalesRepRelacionado(NullSiVacio(r, "SalesRepNombre")))
            {
                esParteRelacionada = true;
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

        return new ClienteAts(tipoIdentificacion, identificacion, tipoCliente, nombre, esParteRelacionada);
    }

    private static bool EsSalesRepRelacionado(string? valor) =>
        valor is not null && string.Equals(valor.Trim(), SalesRepRelacionado, StringComparison.OrdinalIgnoreCase);

    private static string? NullSiVacio(DbDataReader r, string columna)
    {
        var i = r.GetOrdinal(columna);
        return r.IsDBNull(i) ? null : r.GetValue(i)?.ToString();
    }
}
