using Microsoft.Extensions.Logging;
using PsaWeb.Notificaciones;
using PsaWeb.Seguridad;

namespace PsaWeb.Modules.Retenciones;

/// <summary>Resultado de solicitar la anulación de una retención.</summary>
public sealed record ResultadoAnulacion(bool Enviado, string Mensaje);

/// <summary>
/// Envía por correo una solicitud de anulación de un comprobante de retención al
/// supervisor de la empresa. Port de <c>FrmPurcahsesTwhs.btnCancel_Click</c>
/// (el flujo que NO tiene el permiso <c>auCanceTwh</c>): la anulación real la hace
/// alguien más; esto sólo notifica.
/// </summary>
public sealed class SolicitudAnulacionRetencion
{
    private const string Rol = "Supervisor";

    private readonly ISecurityDirectory _directorio;
    private readonly IServicioCorreo _correo;
    private readonly ILogger<SolicitudAnulacionRetencion> _logger;

    public SolicitudAnulacionRetencion(
        ISecurityDirectory directorio, IServicioCorreo correo, ILogger<SolicitudAnulacionRetencion> logger)
    {
        _directorio = directorio;
        _correo = correo;
        _logger = logger;
    }

    public async Task<ResultadoAnulacion> EnviarAsync(
        string ruc,
        string usuario,
        string numeroRetencion,
        string? datilId,
        CancellationToken cancellationToken = default)
    {
        if (!_correo.Disponible)
        {
            return new ResultadoAnulacion(false, "El correo no está configurado; no se puede enviar la solicitud.");
        }

        var supervisores = await _directorio.EmailsPorRolAsync(ruc, Rol, cancellationToken);
        if (supervisores.Count == 0)
        {
            return new ResultadoAnulacion(false,
                "Email de supervisor no configurado, no se ha realizado ninguna acción.");
        }

        var destinatarios = new List<string>(supervisores);
        var emailUsuario = await _directorio.EmailUsuarioAsync(usuario, cancellationToken);
        if (!string.IsNullOrWhiteSpace(emailUsuario))
        {
            destinatarios.Add(emailUsuario);
        }

        var enlace = string.IsNullOrWhiteSpace(datilId)
            ? string.Empty
            : $"<br><a href=\"https://app.datil.co/ver/{datilId}\">Ver comprobante</a>";

        var cuerpo =
            "<p>Estimados.</p><br>" +
            $"<p>El usuario {usuario} ha solicitado eliminar el comprobante de retención # {numeroRetencion}</p>" +
            enlace;

        await _correo.EnviarAsync(
            new MensajeCorreo(destinatarios.Distinct().ToList(),
                "Solicitud de anulación de retención", cuerpo),
            cancellationToken);

        _logger.LogInformation(
            "Solicitud de anulación de retención {Numero} ({Ruc}) enviada a {Destinatarios}",
            numeroRetencion, ruc, string.Join(", ", destinatarios));

        return new ResultadoAnulacion(true,
            $"Solicitud enviada a {destinatarios.Count} destinatario(s).");
    }
}
