using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PsaWeb.Notificaciones;

namespace PsaWeb.Ventas.Tests;

/// <summary>Envío real por SMTP contra un servidor mínimo en proceso: comprueba destinatarios, asunto, cuerpo HTML y que el PDF viaje adjunto.</summary>
public class CorreoSmtpTests
{
    private sealed class ServidorSmtpMinimo : IAsyncDisposable
    {
        private readonly TcpListener _escucha = new(IPAddress.Loopback, 0);
        private readonly Task _bucle;
        public int Puerto => ((IPEndPoint)_escucha.LocalEndpoint).Port;
        public List<string> Destinatarios { get; } = new();
        public string Datos { get; private set; } = "";

        public ServidorSmtpMinimo()
        {
            _escucha.Start();
            _bucle = Atender();
        }

        private async Task Atender()
        {
            using var cliente = await _escucha.AcceptTcpClientAsync();
            using var flujo = cliente.GetStream();
            using var lector = new StreamReader(flujo, Encoding.ASCII);
            using var escritor = new StreamWriter(flujo, new ASCIIEncoding()) { NewLine = "\r\n", AutoFlush = true };
            await escritor.WriteLineAsync("220 servidor de prueba");
            while (await lector.ReadLineAsync() is { } linea)
            {
                if (linea.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) || linea.StartsWith("HELO", StringComparison.OrdinalIgnoreCase)) await escritor.WriteLineAsync("250 ok");
                else if (linea.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase)) await escritor.WriteLineAsync("250 ok");
                else if (linea.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase)) { Destinatarios.Add(linea); await escritor.WriteLineAsync("250 ok"); }
                else if (linea.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await escritor.WriteLineAsync("354 siga");
                    var sb = new StringBuilder();
                    while (await lector.ReadLineAsync() is { } d && d != ".") sb.AppendLine(d);
                    Datos = sb.ToString();
                    await escritor.WriteLineAsync("250 recibido");
                }
                else if (linea.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase)) { await escritor.WriteLineAsync("221 adiós"); break; }
                else await escritor.WriteLineAsync("250 ok");
            }
        }

        public async ValueTask DisposeAsync()
        {
            _escucha.Stop();
            try { await _bucle.WaitAsync(TimeSpan.FromSeconds(3)); } catch { /* el cliente ya cerró */ }
        }
    }

    private static IServicioCorreo Correo(int puerto)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Correo:Servidor"] = "127.0.0.1",
            ["Correo:Puerto"] = puerto.ToString(),
            ["Correo:Ssl"] = "false",
            ["Correo:De"] = "ventas@paredes.test",
        }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddNotificaciones(config);
        return s.BuildServiceProvider().GetRequiredService<IServicioCorreo>();
    }

    [Fact]
    public async Task Envia_a_los_dos_destinatarios_con_el_PDF_adjunto()
    {
        await using var servidor = new ServidorSmtpMinimo();
        var correo = Correo(servidor.Puerto);
        Assert.True(correo.Disponible);

        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% contenido de prueba\n%%EOF");
        await correo.EnviarAsync(new MensajeCorreo(new[] { "conta@sancev.test", "gerencia@sancev.test" }, "[Prefactura PF-0001] Cliente — total 176,44",
            "<p>Hola <b>Contabilidad</b></p>", new[] { new AdjuntoCorreo("PF-0001-Cliente.pdf", pdf, "application/pdf") }));

        Assert.Equal(2, servidor.Destinatarios.Count);
        Assert.Contains(servidor.Destinatarios, d => d.Contains("conta@sancev.test"));
        Assert.Contains(servidor.Destinatarios, d => d.Contains("gerencia@sancev.test"));
        Assert.Contains("Subject:", servidor.Datos);
        Assert.Contains("Content-Type: application/pdf", servidor.Datos);
        Assert.Contains("PF-0001-Cliente.pdf", servidor.Datos);
        Assert.Contains("From: ventas@paredes.test", servidor.Datos);
        // El adjunto viaja en base64: «%PDF-1.4» = JVBERi0xLjQ.
        Assert.Contains("JVBERi0xLjQ", servidor.Datos);
    }

    [Fact]
    public async Task Sin_adjuntos_sigue_funcionando_como_antes()
    {
        await using var servidor = new ServidorSmtpMinimo();
        await Correo(servidor.Puerto).EnviarAsync(new MensajeCorreo(new[] { "a@x.test" }, "Asunto", "<p>cuerpo</p>"));
        Assert.Single(servidor.Destinatarios);
        Assert.DoesNotContain("application/pdf", servidor.Datos);
    }

    [Fact]
    public async Task Sin_servidor_configurado_no_esta_disponible_y_enviar_lanza()
    {
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddNotificaciones(new ConfigurationBuilder().Build());
        var correo = s.BuildServiceProvider().GetRequiredService<IServicioCorreo>();
        Assert.False(correo.Disponible);
        await Assert.ThrowsAsync<InvalidOperationException>(() => correo.EnviarAsync(new MensajeCorreo(new[] { "a@x.test" }, "x", "x")));
    }
}
