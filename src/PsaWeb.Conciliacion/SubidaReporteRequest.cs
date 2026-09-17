namespace PsaWeb.Conciliacion;

/// <summary>Body de <c>POST /conciliacion-sri/api/comprobantes</c> — lo manda la extensión de Chrome.</summary>
public sealed record SubidaReporteRequest(string Ruc, string ContenidoReporte);
