using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PsaWeb.Notificaciones;

/// <summary>Envía correos por SMTP (<see cref="System.Net.Mail"/>). Port de la parte de envío de <c>FrmPurcahsesTwhs</c>.</summary>
internal sealed class SmtpServicioCorreo : IServicioCorreo
{
    private readonly CorreoOptions _options;
    private readonly ILogger<SmtpServicioCorreo> _logger;

    public SmtpServicioCorreo(IOptions<CorreoOptions> options, ILogger<SmtpServicioCorreo> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool Disponible => _options.Configurado;

    public async Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken = default)
    {
        if (!_options.Configurado)
        {
            throw new InvalidOperationException(
                "El correo no está configurado (falta 'Correo:Servidor'). No se puede enviar.");
        }
        if (mensaje.Para.Count == 0)
        {
            throw new ArgumentException("El mensaje no tiene destinatarios.", nameof(mensaje));
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.De),
            Subject = mensaje.Asunto,
            Body = mensaje.CuerpoHtml,
            IsBodyHtml = true,
        };
        foreach (var destino in mensaje.Para.Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            mail.To.Add(destino.Trim());
        }

        var flujos = new List<MemoryStream>();
        try
        {
            foreach (var adjunto in mensaje.Adjuntos ?? Array.Empty<AdjuntoCorreo>())
            {
                var flujo = new MemoryStream(adjunto.Contenido);
                flujos.Add(flujo);
                mail.Attachments.Add(new Attachment(flujo, adjunto.Nombre, adjunto.TipoMime));
            }

            await EnviarAsync(mail, cancellationToken);
        }
        finally
        {
            foreach (var f in flujos) f.Dispose();
        }
    }

    private async Task EnviarAsync(MailMessage mail, CancellationToken cancellationToken)
    {
        using var smtp = new SmtpClient(_options.Servidor!, _options.Puerto)
        {
            EnableSsl = _options.Ssl,
            Credentials = string.IsNullOrEmpty(_options.Usuario)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_options.Usuario, _options.Clave),
        };

        await smtp.SendMailAsync(mail, cancellationToken);
        _logger.LogInformation("Correo enviado a {Para}: {Asunto}", string.Join(", ", mail.To), mail.Subject);
    }
}
