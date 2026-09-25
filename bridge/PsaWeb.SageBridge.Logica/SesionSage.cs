using System;
using Sage.Peachtree.API;

namespace PsaWeb.SageBridge.Logica;

/// <summary>
/// Sesión del SDK de Sage 50 del proceso trabajador (una por proceso, en su hilo STA). Reglas de la F0:
/// <c>VerifyAccess</c> en la misma sesión antes de <c>Open</c>; la compañía se abre por lote y se cierra siempre
/// (si queda abierta, el backup de Sage falla); un error de base de datos deja el proceso inservible y se recicla.
/// </summary>
public sealed class SesionSage : IDisposable
{
    private readonly string _claveAplicacion;
    private PeachtreeSession? _sesion;

    public SesionSage(string claveAplicacion) => _claveAplicacion = claveAplicacion;

    private PeachtreeSession Sesion
    {
        get
        {
            if (_sesion is null)
            {
                var s = new PeachtreeSession();
                s.Begin(_claveAplicacion);
                _sesion = s;
            }

            return _sesion;
        }
    }

    /// <summary>Servidor de Sage configurado en este equipo (el que usa si PeachConnString no trae uno).</summary>
    public string ServidorLocal => Sesion.Configuration.ServerName;

    public CompanyIdentifier Buscar(EmpresaSage empresa) =>
        Sesion.LookupCompanyIdentifier(string.IsNullOrWhiteSpace(empresa.Servidor) ? ServidorLocal : empresa.Servidor, empresa.BaseDatos);

    public AuthorizationResult VerificarAcceso(CompanyIdentifier id) => Sesion.VerifyAccess(id);

    /// <summary>Deja una solicitud de acceso pendiente: aparece en Sage al (re)abrir esa compañía.</summary>
    public AuthorizationResult SolicitarAcceso(CompanyIdentifier id) => Sesion.RequestAccess(id);

    public Company Abrir(CompanyIdentifier id) => Sesion.Open(id);

    public void Cerrar(Company empresa) => Sesion.Close(empresa);

    public void Dispose()
    {
        try
        {
            if (_sesion is { SessionActive: true })
            {
                _sesion.End();
            }
        }
        catch
        {
            // El proceso puede estar envenenado; terminar la sesión no debe ocultar el error original.
        }
    }
}
