using System.Data.Common;
using System.Data.Odbc;
using PsaWeb.Ats.Esquema;

namespace PsaWeb.Ats.Compras;

/// <summary>Código de <c>dicIdentityTypeATS</c> para compras (<c>TransType = 2</c>).</summary>
public readonly record struct CodigoIdentificacionCompras(string ProofTypeId, string IdAts);

/// <summary>Proveedor de Sage 50 ya resuelto para una fila de compras del ATS.</summary>
/// <param name="Encontrado">
/// <c>false</c> cuando la consulta a <c>Vendors</c>+<c>Address</c> no devolvió
/// ninguna fila (equivale a <c>LoadVendor.CorrectLoaded() == false</c> en el
/// `.exe`) — en ese caso <see cref="ArmadorComprasAts"/> no asigna
/// <c>tpIdProv</c>/<c>idProv</c>/<c>pagoExterior</c> en el esquema, tal cual el
/// original.
/// </param>
public sealed record ProveedorAts(
    bool Encontrado,
    string? TipoIdentificacion,
    string Identificacion,
    string Nombre,
    string TipoProveedorExterior,
    pagoExteriorType PagoExterior,
    string? Advertencia)
{
    public static readonly ProveedorAts NoEncontrado = new(
        false, null, string.Empty, string.Empty, string.Empty,
        InfoProveedorExterior.ArmarPagoExterior(InfoProveedorExterior.Ninguna),
        "No se encontró el proveedor en Sage 50.");
}

/// <summary>
/// Lee <c>Vendors</c> + <c>Address</c> por <c>VendorRecordNumber</c> y resuelve
/// el tipo/identificación del proveedor. Port de <c>LoadVendor</c>
/// (<c>ATSfromPeach</c>).
/// </summary>
public static class LectorProveedorAts
{
    private const string Sql = """
        SELECT Vendors.CustomField4 AS Pasaporte,
               Vendors.CustomField3 AS Ruc,
               Address.Country      AS RucAlterno,
               Vendors.CustomField0 AS CodPais,
               Vendors.CustomField1 AS PagoExt,
               Vendors.CustomField2 AS ConvDoble,
               Vendors.OurAccountWithThem AS OurAccountWithThem,
               Vendors.Name AS Nombre
        FROM Vendors, Address
        WHERE Vendors.VendorRecordNumber = Address.VendorRecordNumber
          AND Vendors.VendorRecordNumber = ?
          AND Address.AddressTypeNumber = 0
          AND NOT (Vendors.Name LIKE 'ANULAD%')
        """;

    public static async Task<ProveedorAts> LeerAsync(
        OdbcConnection connection,
        long vendorId,
        IReadOnlyList<CodigoIdentificacionCompras> catalogoIdentificacion,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = new OdbcCommand(Sql, connection);
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.BigInt, Value = vendorId });

        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
        {
            return ProveedorAts.NoEncontrado;
        }

        var nombre = Texto(r, "Nombre");
        var ourAccountWithThem = Texto(r, "OurAccountWithThem");
        var pagoExt = TextoONull(r, "PagoExt");

        var resolucion = ResolverIdentificacion(
            new FilaProveedorCruda(
                OurAccountWithThem: ourAccountWithThem,
                Pasaporte: TextoONull(r, "Pasaporte"),
                Ruc: TextoONull(r, "Ruc"),
                RucAlterno: TextoONull(r, "RucAlterno")),
            catalogoIdentificacion);

        var infoExterior = InfoProveedorExterior.DesdeJson(pagoExt);
        var pagoExterior = InfoProveedorExterior.ArmarPagoExterior(infoExterior);

        // tipoProveedorExterior: si el JSON trae info válida, gana su propio
        // tipo (persona/sociedad) sin importar el camino de resolución. Si no
        // (JSON vacío/ inválido), el `.exe` solo llega a completarlo cuando
        // pasó por la cadena de respaldo (no por dicIdentityTypeATS) y el
        // resultado dio Exterior — ahí dejaba el código crudo de Sage
        // (OurAccountWithThem) por una condición que siempre es verdadera
        // (bug de comparaciones "!=" encadenadas con OR). Si vino por
        // dicIdentityTypeATS, el campo queda vacío tal cual el `.exe`.
        var tipoProveedorExterior = infoExterior.TipoPago != TipoPagoExterior.Ninguno
            ? infoExterior.TipoProveedorTexto()
            : (!resolucion.ViaCatalogo && resolucion.TipoIdentificacion == TiposIdentificacionProveedorAts.Exterior
                ? ourAccountWithThem
                : string.Empty);

        return new ProveedorAts(true, resolucion.TipoIdentificacion, resolucion.Identificacion, nombre, tipoProveedorExterior, pagoExterior, resolucion.Advertencia);
    }

    internal readonly record struct FilaProveedorCruda(string OurAccountWithThem, string? Pasaporte, string? Ruc, string? RucAlterno);

    internal readonly record struct ResolucionIdentificacion(
        string? TipoIdentificacion, string Identificacion, string? Advertencia, bool ViaCatalogo);

    /// <summary>
    /// Resuelve <c>tpIdProv</c>/<c>idProv</c>: primero por
    /// <c>dicIdentityTypeATS</c> (<c>OurAccountWithThem</c> → <c>IdAts</c>,
    /// identificación = <c>Address.Country</c>); si no hay una coincidencia
    /// única, cae en la cadena pasaporte → RUC → <c>Address.Country</c> +
    /// <see cref="TiposIdentificacionProveedorAts.Deducir"/>. Parte pura de
    /// <c>LoadVendor</c>, separada para poder testearla sin ODBC.
    /// </summary>
    internal static ResolucionIdentificacion ResolverIdentificacion(
        FilaProveedorCruda fila, IReadOnlyList<CodigoIdentificacionCompras> catalogo)
    {
        var coincidencias = catalogo.Where(x => x.ProofTypeId == fila.OurAccountWithThem).ToList();

        if (coincidencias.Count == 1)
        {
            var identificacion = fila.RucAlterno ?? string.Empty;
            return new(coincidencias[0].IdAts, identificacion, null, ViaCatalogo: true);
        }

        const string advertencia = "Vendor en Sage 50 tiene información no válida en el campo AccountNumber";

        if (fila.Pasaporte is not null)
        {
            var id = fila.Pasaporte;
            if (id.Length == 0)
            {
                id = fila.Ruc ?? string.Empty;
                if (id.Length == 0)
                {
                    id = fila.RucAlterno ?? string.Empty;
                }

                return new(TiposIdentificacionProveedorAts.Deducir(id), id, advertencia, ViaCatalogo: false);
            }

            return new(TiposIdentificacionProveedorAts.Exterior, id, advertencia, ViaCatalogo: false);
        }

        if (fila.Ruc is not null)
        {
            var id = fila.Ruc;
            if (id.Length == 0)
            {
                id = fila.RucAlterno ?? string.Empty;
            }

            return new(TiposIdentificacionProveedorAts.Deducir(id), id, advertencia, ViaCatalogo: false);
        }

        return new(null, string.Empty, advertencia, ViaCatalogo: false);
    }

    private static string Texto(DbDataReader r, string columna)
    {
        var i = r.GetOrdinal(columna);
        return r.IsDBNull(i) ? string.Empty : r.GetValue(i)?.ToString() ?? string.Empty;
    }

    private static string? TextoONull(DbDataReader r, string columna)
    {
        var i = r.GetOrdinal(columna);
        return r.IsDBNull(i) ? null : r.GetValue(i)?.ToString();
    }
}
