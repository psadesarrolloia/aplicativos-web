namespace PsaWeb.SageBridge.Contratos;

/// <summary>Resultado (JSON en <c>TrabajosSage.ResultadoJson</c>) de un trabajo <see cref="TiposTrabajo.ProbarEmpresa"/>.</summary>
public sealed class ResultadoProbarEmpresa
{
    /// <summary>Nombre de la compañía según Sage.</summary>
    public string Compania { get; set; } = string.Empty;

    /// <summary>Nombre de la base de datos en el SDK (<c>PeachConnString.dbq</c>).</summary>
    public string BaseDatos { get; set; } = string.Empty;

    /// <summary>Resultado de <c>VerifyAccess</c> (Granted, Pending, NoCredentials, …).</summary>
    public string Acceso { get; set; } = string.Empty;

    /// <summary>¿Se pudo abrir la compañía? Falso si Sage todavía no autorizó al Bridge.</summary>
    public bool Abierta { get; set; }

    /// <summary>Segundos que tardó en abrirse (F0: 9–12 s en CPTDC).</summary>
    public double SegundosApertura { get; set; }

    /// <summary>Cuentas contables leídas por SDK como prueba de lectura.</summary>
    public int CuentasLeidas { get; set; }

    /// <summary>Texto para mostrar en la administración (qué hacer si falta autorización).</summary>
    public string Mensaje { get; set; } = string.Empty;
}
