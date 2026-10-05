namespace PsaWeb.Seguridad;

/// <summary>
/// Interruptor de los módulos que <b>escriben en Sage</b> por el Sage Bridge (Ola 2: Compras, Facturas recibidas, Liquidación de importaciones y la
/// administración del Bridge). Sección <c>Escritura</c>. <b>Apagado por defecto</b>: el servidor de producción no activa nada que escriba en Sage hasta el
/// «deploy único de escritura»; en desarrollo se enciende en <c>appsettings.Development.json</c>.
/// Apagado: esos módulos no se registran, sus rutas no existen (404), desaparecen del menú y de «Registrar en Sage» en Conciliación, no corren sus
/// migraciones ni el worker de descarga de XML.
/// </summary>
public sealed class EscrituraOptions
{
    public const string SectionName = "Escritura";

    public bool Habilitada { get; set; }
}
