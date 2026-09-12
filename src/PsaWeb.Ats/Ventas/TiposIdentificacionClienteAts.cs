namespace PsaWeb.Ats.Ventas;

/// <summary>
/// Códigos de tipo de identificación del <b>cliente</b> en el esquema del ATS
/// (Tabla 2: <c>tpIdCliente</c>) — port de la lógica de <c>LoadCustomer</c> +
/// <c>QueryPersonType</c> de <c>ATSfromPeach</c>.
/// </summary>
/// <remarks>
/// Estos códigos son propios del ATS y <b>no coinciden</b> con los que usa
/// <c>PsaWeb.Comprobantes.Sri.TiposIdentificacion</c> para comprobantes
/// electrónicos (ahí "06" es Pasaporte y "08" es Exterior; acá "06" es
/// directamente Exterior y no existe "08"). Por eso el ATS tiene su propio
/// catálogo en vez de reusar el de facturación electrónica.
/// </remarks>
public static class TiposIdentificacionClienteAts
{
    public const string Ruc = "04";
    public const string Cedula = "05";
    public const string Exterior = "06";
    public const string ConsumidorFinal = "07";

    /// <summary>
    /// Deduce el tipo a partir del texto de identificación ya resuelto
    /// (pasaporte / RUC / <c>Address.Country</c> como último recurso — la
    /// resolución del texto es responsabilidad de <see cref="LectorClienteAts"/>).
    /// Port de <c>QueryPersonType</c> + el caso especial de consumidor final
    /// que <c>LoadCustomer</c> resuelve antes de llamarlo.
    /// </summary>
    public static string Deducir(string identificacion)
    {
        if (identificacion == "9999999999999")
        {
            return ConsumidorFinal;
        }

        if (identificacion.Length == 10 && identificacion.All(char.IsDigit))
        {
            return Cedula;
        }

        if (identificacion.Length == 13 && identificacion.All(char.IsDigit))
        {
            return Ruc;
        }

        return Exterior;
    }
}
