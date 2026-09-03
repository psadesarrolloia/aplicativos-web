using System.Data.Common;
using System.Data.Odbc;
using PsaWeb.Comprobantes.Sri;

namespace PsaWeb.Comprobantes.Proveedores;

/// <summary>
/// Lee un proveedor de Sage 50 (<c>Vendors</c> + <c>Address</c>) por su
/// <c>VendorRecordNumber</c> y lo mapea a <see cref="ProveedorSri"/>.
/// Port de <c>LoadVendorPeach</c>.
/// </summary>
/// <remarks>
/// Convenciones de Sage 50 heredadas: la identificación tributaria del proveedor
/// se guarda en <c>Address.Country</c>, y el código de tipo de identificación
/// (04/05/06/08) en <c>Vendors.OurAccountWithThem</c>. El nombre puede venir
/// partido entre <c>Vendors.Name</c> y <c>Vendors.CustomField0</c>.
/// </remarks>
public static class LectorProveedor
{
    private const string Sql = """
        SELECT Address.Country AS Identificacion,
               Vendors.OurAccountWithThem AS TipoId,
               Vendors.Name AS Nombre,
               Vendors.CustomField0 AS NombreExtra,
               Address.AddressLine1 AS Dir1,
               Address.AddressLine2 AS Dir2,
               Vendors.Email AS Email,
               Vendors.PhoneNumber AS Telefono
        FROM Vendors, Address
        WHERE Vendors.VendorRecordNumber = Address.VendorRecordNumber
          AND Vendors.VendorRecordNumber = ?
        """;

    public static async Task<ProveedorSri?> LeerAsync(
        OdbcConnection connection, string vendorRecordNumber, CancellationToken cancellationToken = default)
    {
        await using var command = new OdbcCommand(Sql, connection);
        command.Parameters.Add(new OdbcParameter
        {
            OdbcType = OdbcType.Int,
            Value = int.Parse(vendorRecordNumber),
        });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return Mapear(
            identificacion: Campo(reader, "Identificacion"),
            tipoId: Campo(reader, "TipoId"),
            nombre: Campo(reader, "Nombre"),
            nombreExtra: Campo(reader, "NombreExtra"),
            dir1: Campo(reader, "Dir1"),
            dir2: Campo(reader, "Dir2"),
            email: Campo(reader, "Email"),
            telefono: Campo(reader, "Telefono"));
    }

    internal static ProveedorSri Mapear(
        string identificacion, string tipoId, string nombre, string nombreExtra,
        string dir1, string dir2, string email, string telefono)
    {
        identificacion = identificacion.Trim();
        var errores = new List<string>();

        var razonSocial = (nombre + nombreExtra).Trim();
        var direccion = string.Join(" ",
            new[] { dir1, dir2 }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));

        if (string.IsNullOrWhiteSpace(identificacion))
        {
            errores.Add("Identificación del proveedor vacía.");
        }

        if (razonSocial.ToUpperInvariant().StartsWith("ANULAD"))
        {
            errores.Add("Retención anulada (proveedor marcado ANULAD…).");
        }

        var tipoEsperado = TiposIdentificacion.Deducir(identificacion);
        if (!string.Equals(tipoEsperado, tipoId.Trim(), StringComparison.Ordinal))
        {
            errores.Add(
                $"El tipo de identificación del proveedor (campo AccountNumber = '{tipoId.Trim()}') " +
                $"no coincide con el que corresponde a la identificación ('{tipoEsperado}').");
        }

        var emails = email.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (emails.Length == 0)
        {
            errores.Add("El proveedor no tiene email.");
        }
        foreach (var e in emails)
        {
            if (!EmailSri.EsValido(e))
            {
                errores.Add($"Email del proveedor no válido: {e}");
            }
        }

        return new ProveedorSri
        {
            Identificacion = identificacion,
            TipoIdentificacion = tipoId.Trim(),
            RazonSocial = razonSocial,
            Direccion = direccion,
            Email = email.Trim(),
            Telefono = string.IsNullOrWhiteSpace(telefono) ? null : telefono.Trim(),
            Errores = errores,
        };
    }

    private static string Campo(DbDataReader reader, string columna)
    {
        var i = reader.GetOrdinal(columna);
        return reader.IsDBNull(i) ? string.Empty : reader.GetValue(i)?.ToString() ?? string.Empty;
    }
}
