using System;
using System.Collections.Generic;
using System.Linq;
using Sage.Peachtree.API;
using PsaWeb.SageBridge.Contratos;

namespace PsaWeb.SageBridge.Logica;

/// <summary>Datos de la compañía abierta para el lote en curso.</summary>
public sealed class ContextoLote
{
    public ContextoLote(Company empresa, CompanyIdentifier id, EmpresaSage empresaSage, double segundosApertura)
    {
        Empresa = empresa;
        Identificador = id;
        EmpresaSage = empresaSage;
        SegundosApertura = segundosApertura;
    }

    public Company Empresa { get; }
    public CompanyIdentifier Identificador { get; }
    public EmpresaSage EmpresaSage { get; }
    public double SegundosApertura { get; }
}

/// <summary>Ejecuta un tipo de trabajo sobre la compañía abierta y devuelve el resultado (JSON).</summary>
public interface IManejadorTrabajo
{
    string Tipo { get; }

    string Ejecutar(ContextoLote contexto, TrabajoTomado trabajo);
}

/// <summary>
/// <see cref="TiposTrabajo.ProbarEmpresa"/>: lee las cuentas contables (solo lectura) para confirmar que la
/// compañía abre y responde por SDK.
/// </summary>
public sealed class ManejadorProbarEmpresa : IManejadorTrabajo
{
    public string Tipo => TiposTrabajo.ProbarEmpresa;

    public string Ejecutar(ContextoLote contexto, TrabajoTomado trabajo)
    {
        var cuentas = contexto.Empresa.Factories.AccountFactory.List();
        cuentas.Load();
        return Json.Escribir(new ResultadoProbarEmpresa
        {
            Compania = contexto.Identificador.CompanyName,
            BaseDatos = contexto.Identificador.DatabaseName,
            Acceso = AuthorizationResult.Granted.ToString(),
            Abierta = true,
            SegundosApertura = Math.Round(contexto.SegundosApertura, 1),
            CuentasLeidas = cuentas.Count,
            Mensaje = "Sage autoriza al Bridge en esta empresa: ya se la puede habilitar.",
        });
    }
}

public static class Manejadores
{
    public static IReadOnlyDictionary<string, IManejadorTrabajo> Todos { get; } =
        new IManejadorTrabajo[] { new ManejadorProbarEmpresa() }.ToDictionary(m => m.Tipo);
}
