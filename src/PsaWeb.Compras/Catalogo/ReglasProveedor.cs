using System.Globalization;
using System.Text;
using PsaWeb.Compras.Sri;

namespace PsaWeb.Compras.Catalogo;

/// <summary>
/// Convenciones con que el `.exe` guarda un proveedor en Sage (port de <c>sageVendor</c>: setters de
/// <c>SriPersonId</c>, <c>CompleteName</c>, <c>CompleteAddress</c>, <c>EspTaxPayerNum</c>, <c>HaveToDoAcc</c>) y el ID
/// propuesto para uno nuevo (decisión 7c, §5.1 del plan).
/// </summary>
public static class ReglasProveedor
{
    public const int LargoId = 20;

    /// <summary>Proveedor nuevo con los datos del emisor de la factura (<c>LoadInfoBill</c> con <c>new sageVendor(true)</c>).</summary>
    public static ProveedorSage NuevoDesdeFactura(FacturaRecibida factura, string id, string cuentaGasto, string email)
    {
        var (nombre, cf0) = PartirNombre(factura.Emisor.RazonSocial);
        var (dir1, dir2) = PartirDireccion(factura.Emisor.DireccionMatriz);
        var (tipo, pais, cf4) = Identificacion(factura.Emisor.Ruc);
        return new ProveedorSage(id, null, tipo, nombre, cf0, string.Empty, string.Empty, cf4, dir1, dir2, pais, email,
            string.Empty, Telefono2(factura.Emisor.ObligadoContabilidad, factura.Emisor.ContribuyenteEspecial), cuentaGasto, false);
    }

    /// <summary>
    /// Lo que el `.exe` actualiza de un proveedor existente al abrir su factura: identificación (04/05 → solo
    /// <c>Address.Country</c>; 06/08 → también <c>CustomField4</c>), contribuyente especial y obligado a llevar contabilidad.
    /// </summary>
    public static ProveedorSage ActualizarDesdeFactura(ProveedorSage p, FacturaRecibida factura)
    {
        var ruc = factura.Emisor.Ruc;
        var actualizado = p.TipoIdentificacion switch
        {
            "04" or "05" => p with { Pais = ruc, CustomField4 = string.Empty },
            "06" or "08" => p with { Pais = ruc, CustomField4 = ruc },
            _ => p,
        };
        return actualizado with { Telefono2 = Telefono2(factura.Emisor.ObligadoContabilidad, factura.Emisor.ContribuyenteEspecial) };
    }

    /// <summary>Tipo de identificación de un proveedor nuevo: 13 dígitos terminados en 001 → RUC; 10 dígitos → cédula; resto → pasaporte.</summary>
    public static (string Tipo, string Pais, string CustomField4) Identificacion(string valor)
    {
        if (valor.Length == 13 && valor.All(char.IsDigit) && valor[10..] == "001") return ("04", valor, string.Empty);
        if (valor.Length == 10 && valor.All(char.IsDigit)) return ("05", valor, string.Empty);
        return ("06", valor, valor);
    }

    /// <summary>Nombre: si tiene menos de 39 caracteres va entero a <c>Name</c>; si no, 30 a <c>Name</c> y hasta 30 a <c>CustomField0</c>.</summary>
    public static (string Nombre, string CustomField0) PartirNombre(string? valor)
    {
        if (string.IsNullOrEmpty(valor)) return (string.Empty, string.Empty);
        if (valor.Length < 39) return (valor, string.Empty);
        var resto = valor[30..];
        return (valor[..30], resto.Length < 30 ? resto : resto[..30]);
    }

    /// <summary>Dirección: menos de 30 caracteres en la línea 1; si no, 30 + hasta 30.</summary>
    public static (string Linea1, string Linea2) PartirDireccion(string? valor)
    {
        if (string.IsNullOrEmpty(valor)) return (string.Empty, string.Empty);
        if (valor.Length < 30) return (valor, string.Empty);
        var resto = valor[30..];
        return (valor[..30], resto.Length < 30 ? resto : resto[..30]);
    }

    /// <summary>
    /// <c>PhoneNumber2</c>: «OC-» (obligado a llevar contabilidad) o «SC-», más «CE#número#» si es contribuyente especial.
    /// Corrección C5: el `.exe` releía el número con un <c>Substring</c> mal calculado y lo dejaba truncado a 3 caracteres.
    /// </summary>
    public static string Telefono2(bool obligadoContabilidad, string? contribuyenteEspecial) =>
        (obligadoContabilidad ? "OC-" : "SC-") + (string.IsNullOrEmpty(contribuyenteEspecial) ? string.Empty : $"CE#{contribuyenteEspecial}#");

    /// <summary>
    /// ID propuesto (§5.1): razón social en mayúsculas, sin tildes ni <c>* ? + ½</c>, recortada a 20; si ya existe
    /// (sin distinguir mayúsculas) se agrega <c>-2</c>, <c>-3</c>… recortando la base para no pasar de 20.
    /// </summary>
    public static string IdPropuesto(string razonSocial, IEnumerable<string> idsExistentes)
    {
        var usados = new HashSet<string>(idsExistentes.Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        foreach (var ch in razonSocial.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (ch is '*' or '?' or '+' or '½') continue;
            sb.Append(ch);
        }
        var limpio = string.Join(' ', sb.ToString().Normalize(NormalizationForm.FormC).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var baseId = Recortar(limpio, LargoId);
        if (!usados.Contains(baseId)) return baseId;
        for (var n = 2; ; n++)
        {
            var sufijo = "-" + n.ToString(CultureInfo.InvariantCulture);
            var candidato = Recortar(limpio, LargoId - sufijo.Length) + sufijo;
            if (!usados.Contains(candidato)) return candidato;
        }
    }

    private static string Recortar(string s, int largo) => (s.Length > largo ? s[..largo] : s).TrimEnd();
}
