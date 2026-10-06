using System.Net;

namespace PsaWeb.Identidad;

/// <summary>
/// Envío de los correos de la cuenta (invitación, recuperación de clave, código de verificación, aviso de ingreso nuevo).
/// Identidad no conoce el SMTP: el Host lo conecta con <c>PsaWeb.Notificaciones</c>. Sin SMTP, <see cref="Disponible"/> = false.
/// </summary>
public interface IEnviadorCorreoPlataforma
{
    bool Disponible { get; }

    Task EnviarAsync(string para, string asunto, string cuerpoHtml, CancellationToken cancellationToken = default);
}

/// <summary>Implementación inerte (sin SMTP configurado).</summary>
public sealed class EnviadorCorreoNulo : IEnviadorCorreoPlataforma
{
    public bool Disponible => false;

    public Task EnviarAsync(string para, string asunto, string cuerpoHtml, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("El correo saliente no está configurado (Correo:Servidor).");
}

/// <summary>Textos de los correos de la cuenta. Todo valor variable va codificado en HTML.</summary>
public static class MensajesCuenta
{
    public const string Producto = "Aplicativos Web PSA";

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    private static string Marco(string titulo, string cuerpo) =>
        $"""
        <div style="font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1f2937;max-width:560px">
          <div style="background:#163154;color:#fff;padding:12px 16px;font-weight:600">{Producto}</div>
          <div style="padding:16px">
            <h2 style="color:#163154;font-size:18px;margin:0 0 12px">{E(titulo)}</h2>
            {cuerpo}
            <p style="color:#6b7280;font-size:12px;margin-top:24px">Correo automático, no responder. Si no esperabas este mensaje, avisa a la administración de PSA.</p>
          </div>
        </div>
        """;

    public static (string Asunto, string Html) Invitacion(string nombre, string usuario, string enlace) =>
        ($"{Producto}: activa tu cuenta",
         Marco("Tu cuenta está lista",
            $"""
            <p>Hola {E(nombre)}:</p>
            <p>Se creó tu acceso a {Producto}. Para activarlo, define tu contraseña en este enlace (vale 24 horas):</p>
            <p><a href="{E(enlace)}" style="background:#B40046;color:#fff;padding:8px 14px;text-decoration:none;border-radius:4px">Activar mi cuenta</a></p>
            <p>Después vas a ingresar con este correo. Tu usuario interno es <strong>{E(usuario)}</strong>.</p>
            """));

    public static (string Asunto, string Html) RecuperarClave(string nombre, string enlace) =>
        ($"{Producto}: cambiar la contraseña",
         Marco("Cambiar la contraseña",
            $"""
            <p>Hola {E(nombre)}:</p>
            <p>Pediste cambiar tu contraseña. Usa este enlace (vale 24 horas):</p>
            <p><a href="{E(enlace)}" style="background:#B40046;color:#fff;padding:8px 14px;text-decoration:none;border-radius:4px">Cambiar mi contraseña</a></p>
            <p>Si no lo pediste, ignora este correo: tu contraseña no cambia.</p>
            """));

    public static (string Asunto, string Html) CodigoVerificacion(string nombre, string codigo, string motivo) =>
        ($"{Producto}: código de verificación {codigo}",
         Marco("Código de verificación",
            $"""
            <p>Hola {E(nombre)}:</p>
            <p>Tu código para {E(motivo)} es:</p>
            <p style="font-size:26px;font-weight:700;letter-spacing:4px;color:#163154">{E(codigo)}</p>
            <p>Vale unos minutos. No lo compartas con nadie: PSA nunca te lo va a pedir.</p>
            """));

    public static (string Asunto, string Html) IngresoNuevo(string nombre, string ip, DateTime utc) =>
        ($"{Producto}: ingreso desde un lugar nuevo",
         Marco("Ingreso desde un lugar nuevo",
            $"""
            <p>Hola {E(nombre)}:</p>
            <p>Se ingresó a tu cuenta desde una dirección que no habíamos visto antes:</p>
            <ul><li>IP: <strong>{E(ip)}</strong></li><li>Fecha: {utc.AddHours(-5):dd/MM/yyyy HH:mm} (hora de Ecuador)</li></ul>
            <p>Si fuiste tú, no hace falta hacer nada. Si no, cambia tu contraseña ya y avisa a la administración de PSA.</p>
            """));
}
