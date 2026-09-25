namespace PsaWeb.Compras.Catalogo;

/// <summary>
/// Proveedor de Sage con las convenciones de PSA (port de <c>Sage50MetaData.Model.sageVendor</c> + la consulta de
/// <c>sageVendors</c>): <c>OurAccountWithThem</c> = tipo de identificación, <c>Address.Country</c> = identificación,
/// nombre partido en <c>Name</c> + <c>CustomField0</c>, <c>PhoneNumber2</c> = «OC-»/«SC-» + «CE#n#».
/// </summary>
public sealed record ProveedorSage(
    string Id,
    string? RecordNumber,
    string TipoIdentificacion,
    string Nombre,
    string CustomField0,
    string CustomField1,
    string CustomField3,
    string CustomField4,
    string Direccion1,
    string Direccion2,
    string Pais,
    string Email,
    string Telefono,
    string Telefono2,
    string CuentaGasto,
    bool Inactivo)
{
    /// <summary>Identificación del SRI: <c>Address.Country</c>, si no <c>CustomField3</c>, si no <c>CustomField4</c>.</summary>
    public string Identificacion =>
        !string.IsNullOrEmpty(Pais) ? Pais : !string.IsNullOrEmpty(CustomField3) ? CustomField3 : CustomField4;

    /// <summary>Tipo de identificación válido (04 RUC, 05 cédula, 06 pasaporte, 08 exterior, 09 placa) o vacío.</summary>
    public string TipoIdentificacionSri => TipoIdentificacion is "04" or "05" or "06" or "08" or "09" ? TipoIdentificacion : string.Empty;

    public string NombreCompleto => Nombre + CustomField0;

    public bool EsDelExterior => TipoIdentificacionSri is "06" or "08";

    /// <summary><c>PhoneNumber2</c> contiene «OC-» (obligado a llevar contabilidad).</summary>
    public bool ObligadoContabilidad => Telefono2.Contains("OC-");
}
