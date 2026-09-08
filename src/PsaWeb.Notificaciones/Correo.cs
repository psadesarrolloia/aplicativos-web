namespace PsaWeb.Notificaciones;

/// <summary>Configuración del servidor SMTP (sección <c>Correo</c>). Port de <c>ConfigSettings</c> (Secrets.json).</summary>
public sealed class CorreoOptions
{
    public const string SectionName = "Correo";

    public string? Servidor { get; set; }
    public int Puerto { get; set; } = 587;
    public string? Usuario { get; set; }
    public string? Clave { get; set; }
    public bool Ssl { get; set; }

    /// <summary>Remitente. Fijo por ahora (el <c>.exe</c> usaba <c>info@paredes.com.ec</c>).</summary>
    public string De { get; set; } = "anulaciones@paredes.com.ec";

    public bool Configurado => !string.IsNullOrWhiteSpace(Servidor);
}

/// <summary>Un correo a enviar.</summary>
public sealed record MensajeCorreo(IReadOnlyList<string> Para, string Asunto, string CuerpoHtml);

/// <summary>Envía correos por SMTP. Si el SMTP no está configurado, la impl registrada lanza al usarla.</summary>
public interface IServicioCorreo
{
    bool Disponible { get; }

    Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken = default);
}
