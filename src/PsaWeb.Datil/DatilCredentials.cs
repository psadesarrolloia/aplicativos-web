namespace PsaWeb.Datil;

/// <summary>
/// Credenciales y URL base del API de Datil para una empresa.
/// El consumidor las arma leyendo la tabla <c>DatilAPI</c> de la base PeachEBills.
/// </summary>
/// <param name="ApiKey">Clave del API (cabecera <c>X-Key</c>).</param>
/// <param name="Password">Clave de la firma en Datil (cabecera <c>X-Password</c>).</param>
/// <param name="BaseUrl">
/// URL del servicio de retenciones, terminada en <c>/</c>
/// (ej. <c>https://link.datil.co/.../retenciones/</c>). El cliente agrega
/// <c>issue</c> para emitir y <c>{id}</c> para consultar.
/// </param>
public sealed record DatilCredentials(string ApiKey, string Password, string BaseUrl)
{
    public string IssueUrl => Combine("issue");

    public string StatusUrl(string id) => Combine(id);

    private string Combine(string segment) =>
        BaseUrl.EndsWith('/') ? BaseUrl + segment : BaseUrl + "/" + segment;
}
