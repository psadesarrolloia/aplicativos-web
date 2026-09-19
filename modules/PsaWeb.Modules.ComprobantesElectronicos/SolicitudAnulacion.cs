using Microsoft.Extensions.Logging;
using PsaWeb.Notificaciones;
using PsaWeb.Seguridad;

namespace PsaWeb.Modules.ComprobantesElectronicos;

/// <summary>Resultado de solicitar la anulación de un comprobante.</summary>
public sealed record ResultadoAnulacion(bool Enviado, string Mensaje);

/// <summary>
/// Envía por correo una solicitud de anulación de un comprobante (factura, retención,
/// nota de crédito o liquidación) al supervisor de la empresa. Port de
/// <c>FrmPurcahsesTwhs.btnCancel_Click</c> (el flujo del usuario que NO tiene el
/// permiso «Autorizar anulación»): la anulación real la hace alguien más en el
/// portal del SRI; esto sólo notifica.
/// </summary>
public sealed class SolicitudAnulacion
{
    private const string Rol = "Supervisor";

    private readonly ISecurityDirectory _directorio;
    private readonly IServicioCorreo _correo;
    private readonly ILogger<SolicitudAnulacion> _logger;

    public SolicitudAnulacion(
        ISecurityDirectory directorio, IServicioCorreo correo, ILogger<SolicitudAnulacion> logger)
    {
        _directorio = directorio;
        _correo = correo;
        _logger = logger;
    }

    public async Task<ResultadoAnulacion> EnviarAsync(
        TipoComprobante tipo,
        string ruc,
        string usuario,
        string numero,
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

        var nombre = Tipos.De(tipo).Singular;
        var enlace = string.IsNullOrWhiteSpace(datilId)
            ? string.Empty
            : $"<br><a href=\"https://app.datil.co/ver/{datilId}\">Ver comprobante</a>";

        var cuerpo =
            "<p>Estimados.</p><br>" +
            $"<p>El usuario {usuario} ha solicitado eliminar el comprobante de {nombre} # {numero}</p>" +
            enlace;

        await _correo.EnviarAsync(
            new MensajeCorreo(destinatarios.Distinct().ToList(),
                $"Solicitud de anulación de {nombre}", cuerpo),
            cancellationToken);

        _logger.LogInformation(
            "Solicitud de anulación de {Tipo} {Numero} ({Ruc}) enviada a {Destinatarios}",
            nombre, numero, ruc, string.Join(", ", destinatarios));

        return new ResultadoAnulacion(true,
            $"Solicitud enviada a {destinatarios.Count} destinatario(s).");
    }
}
