namespace PsaWeb.Comprobantes.Sri;

/// <summary>
/// Códigos de tipo de identificación del SRI y su deducción a partir de la
/// cédula / RUC (port de <c>QueryPersonType</c>).
/// </summary>
public static class TiposIdentificacion
{
    public const string Ruc = "04";
    public const string Cedula = "05";
    public const string Pasaporte = "06";
    public const string ConsumidorFinal = "07";
    public const string Exterior = "08";

    public static string Deducir(string? cedulaORuc)
    {
        if (string.IsNullOrEmpty(cedulaORuc))
        {
            return Exterior;
        }

        var soloDigitos = cedulaORuc.All(char.IsDigit);

        return (cedulaORuc.Length, soloDigitos) switch
        {
            (10, true) => Cedula,
            (13, true) when cedulaORuc == "9999999999999" => ConsumidorFinal,
            (13, true) => Ruc,
            _ => Exterior,
        };
    }
}
