namespace PsaWeb.Ats.Compras;

/// <summary>
/// Códigos de tipo de identificación del <b>proveedor</b> en el esquema del
/// ATS (<c>detalleComprasType.tpIdProv</c>) — port de <c>QueryPersonType</c>
/// (<c>ATSfromPeach</c>), usado como respaldo cuando <c>dicIdentityTypeATS</c>
/// no tiene una fila para el código de Sage del proveedor.
/// </summary>
/// <remarks>
/// Es un catálogo más — <b>ni</b> el de <see cref="Ventas.TiposIdentificacionClienteAts"/>
/// (04/05/06/07) <b>ni</b> el de comprobantes electrónicos
/// (<c>PsaWeb.Comprobantes.Sri.TiposIdentificacion</c>, 04/05/06/08): acá RUC
/// es "01", cédula "02" y exterior "03", calcado del <c>QueryPersonType</c>
/// original.
/// </remarks>
public static class TiposIdentificacionProveedorAts
{
    public const string Ruc = "01";
    public const string Cedula = "02";
    public const string Exterior = "03";

    public static string Deducir(string identificacion)
    {
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
