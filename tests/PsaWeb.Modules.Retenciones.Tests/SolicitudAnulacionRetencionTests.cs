using Microsoft.Extensions.Logging.Abstractions;
using PsaWeb.Modules.Retenciones;
using PsaWeb.Notificaciones;
using PsaWeb.Seguridad;

namespace PsaWeb.Modules.Retenciones.Tests;

public class SolicitudAnulacionRetencionTests
{
    private sealed class CorreoFake(bool disponible = true) : IServicioCorreo
    {
        public MensajeCorreo? Ultimo;
        public bool Disponible { get; } = disponible;

        public Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken = default)
        {
            Ultimo = mensaje;
            return Task.CompletedTask;
        }
    }

    private sealed class DirectorioFake : ISecurityDirectory
    {
        public string? EmailUsuario;
        public IReadOnlyList<string> Supervisores = Array.Empty<string>();

        public Task<string?> EmailUsuarioAsync(string usuario, CancellationToken ct = default)
            => Task.FromResult(EmailUsuario);
        public Task<IReadOnlyList<string>> EmailsPorRolAsync(string ruc, string rol = "Supervisor", CancellationToken ct = default)
            => Task.FromResult(Supervisores);

        public Task<IReadOnlyList<EmpresaDelUsuario>> EmpresasDelUsuarioAsync(string usuario, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmpresaDelUsuario>>(Array.Empty<EmpresaDelUsuario>());
        public Task<IReadOnlyDictionary<string, int>> ContarEmpresasAsync(IEnumerable<string> usuarios, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());
        public Task<IReadOnlySet<string>> PermisosAsync(string usuario, string ruc, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }

    private static SolicitudAnulacionRetencion Crear(CorreoFake correo, DirectorioFake dir) =>
        new(dir, correo, NullLogger<SolicitudAnulacionRetencion>.Instance);

    [Fact]
    public async Task Envia_al_supervisor_y_al_usuario_con_el_enlace_a_Datil()
    {
        var correo = new CorreoFake();
        var dir = new DirectorioFake
        {
            EmailUsuario = "juan@paredes.com.ec",
            Supervisores = new[] { "sup@paredes.com.ec" },
        };

        var r = await Crear(correo, dir).EnviarAsync(
            ruc: "1791313747001", usuario: "juan", numeroRetencion: "001-003-000000123", datilId: "abc123");

        Assert.True(r.Enviado);
        Assert.NotNull(correo.Ultimo);
        Assert.Equal("Solicitud de anulación de retención", correo.Ultimo!.Asunto);
        Assert.Contains("sup@paredes.com.ec", correo.Ultimo.Para);
        Assert.Contains("juan@paredes.com.ec", correo.Ultimo.Para);
        Assert.Contains("001-003-000000123", correo.Ultimo.CuerpoHtml);
        Assert.Contains("https://app.datil.co/ver/abc123", correo.Ultimo.CuerpoHtml);
        Assert.Contains("juan", correo.Ultimo.CuerpoHtml);
    }

    [Fact]
    public async Task Sin_supervisor_no_envia_y_avisa()
    {
        var correo = new CorreoFake();
        var r = await Crear(correo, new DirectorioFake { Supervisores = Array.Empty<string>() })
            .EnviarAsync("1791313747001", "juan", "001-003-000000123", "abc123");

        Assert.False(r.Enviado);
        Assert.Contains("supervisor", r.Mensaje);
        Assert.Null(correo.Ultimo);
    }

    [Fact]
    public async Task Sin_SMTP_configurado_no_envia()
    {
        var correo = new CorreoFake(disponible: false);
        var r = await Crear(correo, new DirectorioFake { Supervisores = new[] { "sup@paredes.com.ec" } })
            .EnviarAsync("1791313747001", "juan", "001-003-000000123", "abc123");

        Assert.False(r.Enviado);
        Assert.Contains("correo no está configurado", r.Mensaje);
    }

    [Fact]
    public async Task Sin_datilId_no_incluye_enlace()
    {
        var correo = new CorreoFake();
        await Crear(correo, new DirectorioFake { Supervisores = new[] { "sup@paredes.com.ec" } })
            .EnviarAsync("1791313747001", "juan", "001-003-000000123", datilId: null);

        Assert.DoesNotContain("app.datil.co", correo.Ultimo!.CuerpoHtml);
    }

    [Fact]
    public void CorreoOptions_no_configurado_por_defecto()
    {
        Assert.False(new CorreoOptions().Configurado);
        Assert.True(new CorreoOptions { Servidor = "smtp.paredes.com.ec" }.Configurado);
        Assert.Equal("anulaciones@paredes.com.ec", new CorreoOptions().De);
        Assert.Equal(587, new CorreoOptions().Puerto);
    }
}
