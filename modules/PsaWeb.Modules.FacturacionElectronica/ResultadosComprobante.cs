namespace PsaWeb.Modules.FacturacionElectronica;

public enum EstadoComprobante
{
    Emitido,
    DryRun,
    Error,
}

/// <summary>Resultado de procesar un comprobante (factura / NC / liquidación).</summary>
public sealed record ResultadoComprobante(
    EstadoComprobante Estado,
    string PostOrder,
    string NumeroCompleto,
    string? DatilId,
    int? RefId,
    IReadOnlyList<string> Mensajes)
{
    public bool Ok => Estado is EstadoComprobante.Emitido or EstadoComprobante.DryRun;

    public static ResultadoComprobante Error(string postOrder, string numero, params string[] mensajes) =>
        new(EstadoComprobante.Error, postOrder, numero, null, null, mensajes);
}

/// <summary>Resumen de un "procesar lote" de un tipo de comprobante.</summary>
public sealed record ResumenLote(
    string TipoDoc,
    DateTimeOffset Inicio,
    DateTimeOffset Fin,
    bool DryRun,
    IReadOnlyList<ResultadoComprobante> Resultados)
{
    public int Total => Resultados.Count;
    public int Emitidos => Resultados.Count(r => r.Estado == EstadoComprobante.Emitido);
    public int EnDryRun => Resultados.Count(r => r.Estado == EstadoComprobante.DryRun);
    public int ConErrores => Resultados.Count(r => r.Estado == EstadoComprobante.Error);
}
