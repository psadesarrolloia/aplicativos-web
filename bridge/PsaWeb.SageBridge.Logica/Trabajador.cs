using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using Sage.Peachtree.API;
using PsaWeb.SageBridge.Contratos;

namespace PsaWeb.SageBridge.Logica;

/// <summary>
/// Ciclo del proceso trabajador: elige la empresa con el trabajo listo más antiguo (fuera de su ventana de
/// mantenimiento), toma un lote de esa empresa, abre la compañía UNA vez, ejecuta los trabajos y la cierra.
/// </summary>
public sealed class Trabajador
{
    public const int CodigoNormal = 0;
    public const int CodigoReciclar = 3;

    private readonly Configuracion _cfg;
    private readonly ColaSql _cola;
    private readonly ResolutorEmpresas _resolutor;
    private readonly Action<string> _log;
    private readonly string _instancia;
    private readonly DateTime _iniciadoUtc = DateTime.UtcNow;
    private readonly string _versionAnfitrion;
    private readonly string _versionLogica;
    private DateTime _proximaConversionUtc = DateTime.MinValue;

    public Trabajador(Configuracion cfg, Action<string> log)
    {
        _cfg = cfg;
        _log = log;
        _cola = new ColaSql(cfg.PlataformaConnectionString);
        _resolutor = new ResolutorEmpresas(cfg);
        _instancia = $"{Environment.MachineName}/{cfg.Instancia}";
        _versionAnfitrion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "?";
        _versionLogica = typeof(Trabajador).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
    }

    public int Ejecutar(WaitHandle detener)
    {
        var liberados = _cola.LiberarPropios(_instancia);
        _log($"Trabajador iniciado: instancia {_instancia}, lógica {_versionLogica}, usuario {Environment.UserName}. " +
             $"Trabajos propios devueltos a la cola: {liberados}.");

        using var sesion = new SesionSage(_cfg.ClaveAplicacion());
        while (!detener.WaitOne(0))
        {
            try
            {
                Latir("esperando trabajos", null);
                ProgramarConversiones(DateTime.UtcNow, DateTime.Now);
                var elegido = ElegirEmpresa(DateTime.UtcNow, DateTime.Now, out var soloProbar);
                if (elegido is null)
                {
                    detener.WaitOne(TimeSpan.FromSeconds(_cfg.IntervaloSondeoSegundos));
                    continue;
                }

                var ahora = DateTime.UtcNow;
                var lote = _cola.TomarLote(elegido, _instancia, ahora, ahora.AddMinutes(_cfg.LeaseMinutos), _cfg.MaximoTrabajosPorLote, soloProbar);
                if (lote.Count == 0)
                {
                    continue; // otro ciclo lo tomó entre la consulta y el UPDATE
                }

                if (ProcesarLote(sesion, elegido, lote) == CodigoReciclar)
                {
                    Latir("reciclando el proceso (error de base del SDK)", null);
                    return CodigoReciclar;
                }
            }
            catch (Exception ex)
            {
                _log("Error en el ciclo: " + ex);
                if (ClasificadorErroresSage.Clasificar(ex.GetType().Name, ex.Message) == AccionAnteError.ReciclarProceso)
                {
                    return CodigoReciclar;
                }

                detener.WaitOne(TimeSpan.FromSeconds(30));
            }
        }

        Latir("detenido", null);
        _log("Trabajador detenido.");
        return CodigoNormal;
    }

    /// <summary>
    /// Empresa a atender: la del trabajo listo más antiguo que no esté en su ventana de mantenimiento. En una empresa
    /// no habilitada solo se atiende <c>ProbarEmpresa</c> (<paramref name="soloProbar"/>).
    /// </summary>
    private string? ElegirEmpresa(DateTime ahoraUtc, DateTime ahoraLocal, out bool soloProbar)
    {
        soloProbar = false;
        var empresas = _cola.LeerEmpresas();
        foreach (var candidata in _cola.EmpresasConPendientes(ahoraUtc))
        {
            empresas.TryGetValue(candidata.Ruc, out var cfgEmpresa);
            if (_cfg.Ventana(cfgEmpresa?.Ventana).Contiene(ahoraLocal))
            {
                continue;
            }

            var habilitada = cfgEmpresa?.Habilitada ?? false;
            if (!habilitada && !candidata.HayProbar)
            {
                continue; // sus trabajos esperan a que la habiliten
            }

            soloProbar = !habilitada;
            return candidata.Ruc;
        }

        return null;
    }

    private int ProcesarLote(SesionSage sesion, string ruc, List<TrabajoTomado> lote)
    {
        Latir($"procesando {lote.Count} trabajo(s) de {ruc}", ruc);
        _log($"Lote de {ruc}: {string.Join(", ", lote.Select(t => $"#{t.Id} {t.Tipo}"))}.");

        EmpresaSage empresa;
        try
        {
            empresa = _resolutor.Resolver(ruc);
        }
        catch (Exception ex)
        {
            FallarTodos(lote, "No se pudo ubicar la compañía de Sage: " + ex.Message);
            return CodigoNormal;
        }

        if (!_cfg.BasePermitida(empresa.BaseDatos))
        {
            FallarTodos(lote, $"La base «{empresa.BaseDatos}» no está en SoloBases de la configuración del Bridge: no se abre.");
            return CodigoNormal;
        }

        CompanyIdentifier id;
        AuthorizationResult acceso;
        try
        {
            id = sesion.Buscar(empresa);
            acceso = sesion.VerificarAcceso(id);
            _cola.GuardarAcceso(ruc, acceso.ToString());
        }
        catch (Exception ex)
        {
            return ManejarErrorDeLote(lote, ex, "ubicar la compañía o verificar el acceso");
        }

        if (acceso != AuthorizationResult.Granted)
        {
            ResponderSinAcceso(sesion, id, acceso, lote);
            return CodigoNormal;
        }

        Company compania;
        var reloj = Stopwatch.StartNew();
        try
        {
            compania = sesion.Abrir(id);
        }
        catch (Exception ex)
        {
            return ManejarErrorDeLote(lote, ex, "abrir la compañía");
        }

        var contexto = new ContextoLote(compania, id, empresa, reloj.Elapsed.TotalSeconds, _cfg);
        _log($"{id.CompanyName} abierta en {contexto.SegundosApertura:0.0} s.");
        try
        {
            for (var i = 0; i < lote.Count; i++)
            {
                var trabajo = lote[i];
                _cola.RenovarLease(trabajo.Id, _instancia, DateTime.UtcNow.AddMinutes(_cfg.LeaseMinutos));
                if (EjecutarTrabajo(contexto, trabajo) == CodigoReciclar)
                {
                    // Los que quedaban sin tocar vuelven a la cola sin consumir intento.
                    foreach (var resto in lote.Skip(i + 1))
                    {
                        _cola.Reprogramar(resto.Id, _instancia, null, null, descontarIntento: true);
                    }

                    return CodigoReciclar;
                }
            }
        }
        finally
        {
            try
            {
                sesion.Cerrar(compania);
                _log($"{id.CompanyName} cerrada.");
            }
            catch (Exception ex)
            {
                _log($"No se pudo cerrar {id.CompanyName}: {ex.Message}");
            }
        }

        return CodigoNormal;
    }

    private int EjecutarTrabajo(ContextoLote contexto, TrabajoTomado trabajo)
    {
        if (!Manejadores.Todos.TryGetValue(trabajo.Tipo, out var manejador))
        {
            _cola.Fallar(trabajo.Id, _instancia, $"Esta versión del Bridge ({_versionLogica}) no sabe ejecutar trabajos «{trabajo.Tipo}».");
            return CodigoNormal;
        }

        try
        {
            var resultado = manejador.Ejecutar(contexto, trabajo);
            _cola.Completar(trabajo.Id, _instancia, resultado);
            _log($"#{trabajo.Id} {trabajo.Tipo}: hecho.{ResumenConversion(trabajo.Tipo, resultado)}");
            if (trabajo.Tipo == TiposTrabajo.GuardarOc && _cfg.ConvertirAlGuardar)
            {
                EncolarConversion(trabajo.Ruc, "al guardar una OC");
            }

            return CodigoNormal;
        }
        catch (RechazoTrabajoException ex)
        {
            _log($"#{trabajo.Id} {trabajo.Tipo}: rechazado. {ex.Message}");
            _cola.Fallar(trabajo.Id, _instancia, ex.Message);
            return CodigoNormal;
        }
        catch (Exception ex)
        {
            return ManejarError(trabajo, ex);
        }
    }

    /// <summary>Error antes de poder ejecutar trabajos (ubicar/abrir la compañía): se aplica a todo el lote.</summary>
    private int ManejarErrorDeLote(List<TrabajoTomado> lote, Exception ex, string etapa)
    {
        _log($"Error al {etapa}: {ex}");
        var codigo = CodigoNormal;
        foreach (var t in lote)
        {
            if (ManejarError(t, ex) == CodigoReciclar)
            {
                codigo = CodigoReciclar;
            }
        }

        return codigo;
    }

    private int ManejarError(TrabajoTomado trabajo, Exception ex)
    {
        var mensaje = $"{ex.GetType().Name}: {ex.Message}";
        switch (ClasificadorErroresSage.Clasificar(ex.GetType().Name, ex.Message))
        {
            case AccionAnteError.ReciclarProceso:
                // El proceso queda inservible igual (se recicla), pero el trabajo no se reintenta sin fin:
                // un error de base permanente (p. ej. la carpeta de la compañía no existe) termina en Error.
                if (PoliticaReintentos.QuedanIntentos(trabajo.Intentos))
                {
                    _log($"#{trabajo.Id}: error de base del SDK, se reintenta tras reciclar el proceso. {mensaje}");
                    _cola.Reprogramar(trabajo.Id, _instancia, $"Error de base de Sage (intento {trabajo.Intentos}); se reintenta: {mensaje}",
                        DateTime.UtcNow + PoliticaReintentos.Demora(trabajo.Intentos));
                }
                else
                {
                    _log($"#{trabajo.Id}: error de base del SDK, sin más intentos. {mensaje}");
                    _cola.Fallar(trabajo.Id, _instancia, $"Error de base de Sage tras {trabajo.Intentos} intentos: {mensaje}");
                }

                return CodigoReciclar;

            case AccionAnteError.Reintentar when PoliticaReintentos.QuedanIntentos(trabajo.Intentos):
                var demora = PoliticaReintentos.Demora(trabajo.Intentos);
                _log($"#{trabajo.Id}: compañía ocupada, se reintenta en {demora.TotalMinutes:0} min. {mensaje}");
                _cola.Reprogramar(trabajo.Id, _instancia, $"Compañía ocupada (intento {trabajo.Intentos}); se reintenta: {mensaje}",
                    DateTime.UtcNow + demora);
                return CodigoNormal;

            case AccionAnteError.SinAutorizacion:
                _cola.Fallar(trabajo.Id, _instancia, "Sage no autorizó al Bridge en esta empresa. Prueba la empresa desde la administración del Bridge. " + mensaje);
                return CodigoNormal;

            default:
                _log($"#{trabajo.Id}: error. {ex}");
                _cola.Fallar(trabajo.Id, _instancia, mensaje);
                return CodigoNormal;
        }
    }

    private void ResponderSinAcceso(SesionSage sesion, CompanyIdentifier id, AuthorizationResult acceso, List<TrabajoTomado> lote)
    {
        foreach (var t in lote)
        {
            if (t.Tipo != TiposTrabajo.ProbarEmpresa)
            {
                _cola.Fallar(t.Id, _instancia, $"Sage no autorizó al Bridge en esta empresa (estado {acceso}). Prueba la empresa desde la administración del Bridge.");
                continue;
            }

            var estado = acceso;
            if (acceso == AuthorizationResult.None || acceso == AuthorizationResult.NoCredentials)
            {
                estado = sesion.SolicitarAcceso(id);
                _cola.GuardarAcceso(t.Ruc, estado.ToString());
                _log($"{id.CompanyName}: solicitud de acceso enviada (estado {estado}).");
            }

            var mensaje = estado switch
            {
                AuthorizationResult.Pending =>
                    "Solicitud de acceso pendiente. Abre esta empresa en Sage (si ya estaba abierta, ciérrala y vuelve a abrirla), " +
                    "elige «Always allow access» y prueba otra vez.",
                AuthorizationResult.Denied =>
                    "Sage tiene NEGADO el acceso al Bridge en esta empresa. Hay que quitar la negación en Sage antes de volver a probar.",
                AuthorizationResult.CompanyLocked or AuthorizationResult.LoginRestricted =>
                    $"La compañía no admite el acceso ahora ({estado}). Prueba más tarde.",
                _ => $"Sage respondió «{estado}» al pedir acceso.",
            };

            _cola.Completar(t.Id, _instancia, Json.Escribir(new ResultadoProbarEmpresa
            {
                Compania = id.CompanyName,
                BaseDatos = id.DatabaseName,
                Acceso = estado.ToString(),
                Abierta = false,
                Mensaje = mensaje,
            }));
        }
    }

    /// <summary>
    /// Reemplazo del timer del worker COM: cada <see cref="Configuracion.ConversionMinutos"/> encola un <c>ConvertirOcs</c> por
    /// empresa habilitada que no esté en su ventana de mantenimiento (y que no tenga ya uno en cola). 0 = desactivado.
    /// </summary>
    private void ProgramarConversiones(DateTime ahoraUtc, DateTime ahoraLocal)
    {
        if (_cfg.ConversionMinutos <= 0 || ahoraUtc < _proximaConversionUtc) return;
        _proximaConversionUtc = ahoraUtc.AddMinutes(_cfg.ConversionMinutos);
        foreach (var e in _cola.LeerEmpresas().Where(e => e.Value.Habilitada && !_cfg.Ventana(e.Value.Ventana).Contiene(ahoraLocal)))
        {
            EncolarConversion(e.Key, "periódica");
        }
    }

    private void EncolarConversion(string ruc, string motivo)
    {
        try
        {
            if (_cola.EncolarSiNoHayPendiente(ruc, TiposTrabajo.ConvertirOcs, null, $"auto-{DateTime.UtcNow:yyyyMMddHHmmssfff}", "SageBridge"))
            {
                _log($"{ruc}: conversión de OC encolada ({motivo}).");
            }
        }
        catch (Exception ex)
        {
            _log($"{ruc}: no se pudo encolar la conversión de OC: {ex.Message}");
        }
    }

    private static string ResumenConversion(string tipo, string resultado)
    {
        if (tipo != TiposTrabajo.ConvertirOcs) return string.Empty;
        var r = Json.Leer<ResultadoConvertirOcs>(resultado);
        return $" {r.Pendientes} pendiente(s), {r.Convertidas.Count} convertida(s), {r.SoloSincronizadas} solo anotada(s), {r.Errores.Count} error(es)." +
               string.Concat(r.Errores.Select(e => Environment.NewLine + "  " + e));
    }

    private void FallarTodos(List<TrabajoTomado> lote, string error)
    {
        _log(error);
        foreach (var t in lote)
        {
            _cola.Fallar(t.Id, _instancia, error);
        }
    }

    private void Latir(string estado, string? empresaAbierta)
    {
        try
        {
            _cola.Latir(_instancia, _iniciadoUtc, $"{Environment.UserDomainName}\\{Environment.UserName}",
                _versionAnfitrion, _versionLogica, estado, empresaAbierta);
        }
        catch (Exception ex)
        {
            _log("No se pudo registrar el latido: " + ex.Message);
        }
    }
}
