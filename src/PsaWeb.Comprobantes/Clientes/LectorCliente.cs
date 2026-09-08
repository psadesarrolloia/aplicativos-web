using System.Data.Common;
using System.Data.Odbc;
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Comprobantes.Clientes;

/// <summary>
/// Lee un cliente de Sage 50 (<c>Customers</c> + <c>Address</c>) por su
/// <c>CustomerRecordNumber</c> y lo mapea a <see cref="ClienteSri"/>.
/// Port de <c>LoadCustomerPeach</c>.
/// </summary>
/// <remarks>
/// Convenciones de Sage 50 heredadas: la identificación tributaria puede venir en
/// <c>Customers.CustomField4</c> o en <c>Address.Country</c>; el pasaporte en
/// <c>Customers.CustomField5</c>; el nombre partido entre <c>Customer_Bill_Name</c>
/// y <c>CustomField1</c>. Si la identificación principal viene vacía y el cliente
/// tiene varias direcciones, se toma la primera dirección con <c>Country</c>.
/// </remarks>
public static class LectorCliente
{
    private const string SqlCliente = """
        SELECT Address.CustomerRecordNumber AS RecordNumber,
               Customers.CustomField5 AS Pasaporte,
               Customers.CustomField4 AS Ruc,
               Address.Country AS Ruc2,
               CONCAT(CONCAT(Customers.Customer_Bill_Name, ' '), Customers.CustomField1) AS Nombre,
               Address.AddressLine1 AS Dir1,
               Address.AddressLine2 AS Dir2,
               Customers.eMail_Address AS Email,
               Customers.Phone_Number AS Telefono,
               Customers.FAX_Number AS Fax
        FROM Customers, Address
        WHERE Address.CustomerRecordNumber = Customers.CustomerRecordNumber
          AND Address.CustomerRecordNumber = ?
        """;

    private const string SqlDirecciones = """
        SELECT Country, AddressLine1, AddressLine2
        FROM Address
        WHERE CustomerRecordNumber = ?
        """;

    public static async Task<ClienteSri?> LeerAsync(
        OdbcConnection connection, string customerRecordNumber, CancellationToken cancellationToken = default)
    {
        var id = int.Parse(customerRecordNumber);

        string pasaporte, ruc, ruc2, nombre, dir1, dir2, email, telefono, fax;
        await using (var command = new OdbcCommand(SqlCliente, connection))
        {
            command.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = id });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            pasaporte = Campo(reader, "Pasaporte");
            ruc = Campo(reader, "Ruc");
            ruc2 = Campo(reader, "Ruc2");
            nombre = Campo(reader, "Nombre");
            dir1 = Campo(reader, "Dir1");
            dir2 = Campo(reader, "Dir2");
            email = Campo(reader, "Email");
            telefono = Campo(reader, "Telefono");
            fax = Campo(reader, "Fax");
        }

        var direcciones = new List<DireccionSage>();
        await using (var command = new OdbcCommand(SqlDirecciones, connection))
        {
            command.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = id });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                direcciones.Add(new DireccionSage(
                    Campo(reader, "Country"), Campo(reader, "AddressLine1"), Campo(reader, "AddressLine2")));
            }
        }

        return Mapear(pasaporte, ruc, ruc2, nombre, dir1, dir2, email, telefono, fax, direcciones);
    }

    internal readonly record struct DireccionSage(string Country, string AddressLine1, string AddressLine2);

    internal static ClienteSri Mapear(
        string pasaporte, string ruc, string ruc2, string nombre,
        string dir1, string dir2, string email, string telefono, string fax,
        IReadOnlyList<DireccionSage> direccionesExtra)
    {
        pasaporte = pasaporte.Trim();
        ruc = ruc.Trim();
        ruc2 = ruc2.Trim();

        // Si la identificación principal viene vacía y hay varias direcciones,
        // se toma la primera con Country y su dirección.
        string? direccionElegida = null;
        if (string.IsNullOrEmpty(ruc2) && direccionesExtra.Count > 1
            && direccionesExtra.Any(d => !string.IsNullOrWhiteSpace(d.Country)))
        {
            var d = direccionesExtra.First(x => !string.IsNullOrWhiteSpace(x.Country));
            ruc2 = d.Country.Trim();
            direccionElegida = Combinar(d.AddressLine1, d.AddressLine2);
        }

        string identificacion;
        string tipo;
        if (pasaporte.Length > 1)
        {
            identificacion = pasaporte;
            tipo = TiposIdentificacion.Pasaporte;
        }
        else
        {
            identificacion = ruc.Length > 0 ? ruc : ruc2;
            tipo = TiposIdentificacion.Deducir(identificacion);
        }

        var direccion = direccionElegida ?? Combinar(dir1, dir2);
        var razonSocial = nombre.Trim();

        var errores = new List<string>();

        var emails = email.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (emails.Length == 0)
        {
            errores.Add("El cliente no tiene email.");
        }
        foreach (var e in emails)
        {
            if (!EmailSri.EsValido(e))
            {
                errores.Add($"Email del cliente no válido: {e}");
            }
        }

        if (identificacion.Length == 0)
        {
            errores.Add("Revise la cédula / RUC / identificación del cliente.");
        }

        return new ClienteSri
        {
            Identificacion = identificacion,
            TipoIdentificacion = tipo,
            RazonSocial = razonSocial,
            Direccion = direccion,
            Email = email.Trim(),
            Telefono = string.IsNullOrWhiteSpace(telefono) ? null : telefono.Trim(),
            Fax = fax.Trim().Length > 1 ? fax.Trim() : null,
            Errores = errores,
        };
    }

    private static string Combinar(string? a, string? b)
    {
        var partes = new[] { a, b }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim());
        return string.Join(" ", partes);
    }

    private static string Campo(DbDataReader reader, string columna)
    {
        var i = reader.GetOrdinal(columna);
        return reader.IsDBNull(i) ? string.Empty : reader.GetValue(i)?.ToString() ?? string.Empty;
    }
}
