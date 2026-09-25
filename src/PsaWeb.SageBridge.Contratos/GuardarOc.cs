using System.Collections.Generic;

namespace PsaWeb.SageBridge.Contratos;

// Contratos del trabajo GuardarOc (F3). La web los escribe con System.Text.Json (orden de declaración) y el Bridge los lee
// con DataContractJsonSerializer (orden alfabético): por eso las propiedades van en ORDEN ALFABÉTICO. Fechas como texto
// «yyyy-MM-dd». Ver §6.1 del plan de la Ola 2.

/// <summary>Payload de <see cref="TiposTrabajo.GuardarOc"/>: la Purchase Order ya armada por la web (<c>ArmadorOc</c>).</summary>
public sealed class PayloadGuardarOc
{
    /// <summary>Cuenta por pagar de la OC (20000).</summary>
    public string CuentaPorPagar { get; set; } = string.Empty;

    /// <summary><c>ShipToState</c>: 01 crédito / 02 costo.</summary>
    public string EstadoSustento { get; set; } = string.Empty;

    /// <summary>Fecha de emisión de la factura (<c>yyyy-MM-dd</c>).</summary>
    public string Fecha { get; set; } = string.Empty;

    /// <summary>Fecha de registro (<c>GoodThruDate</c>, <c>yyyy-MM-dd</c>).</summary>
    public string FechaRegistro { get; set; } = string.Empty;

    public List<LineaOcContrato> Lineas { get; set; } = new List<LineaOcContrato>();

    /// <summary>Nº de factura <c>001-001-000000123</c> (<c>TermsDescription</c> y <c>ShipToAddress1</c>). Con el proveedor identifica la OC.</summary>
    public string NumeroFactura { get; set; } = string.Empty;

    /// <summary>Nº de OC elegido por el digitador; vacío = el Bridge asigna el siguiente del <see cref="PrefijoOc"/>.</summary>
    public string? NumeroOc { get; set; }

    /// <summary>Nº de retención elegido por el digitador; vacío = el Bridge asigna el siguiente de la <see cref="SerieRetencion"/>.</summary>
    public string? NumeroRetencion { get; set; }

    /// <summary>Prefijo de la numeración automática de la OC (<c>OC</c>).</summary>
    public string PrefijoOc { get; set; } = "OC";

    public ProveedorContrato Proveedor { get; set; } = new ProveedorContrato();

    /// <summary>La OC lleva nº de retención (el `.exe` lo asigna si alguna línea tiene retención, aunque sea 332 al 0 %).</summary>
    public bool RequiereRetencion { get; set; }

    /// <summary>Primer secuencial de retención de la serie (<c>Establishments.startNumerationTwh</c>); 0 = sin mínimo.</summary>
    public int SecuencialRetencionInicial { get; set; }

    /// <summary>Serie de la retención <c>001-001</c> (establecimiento electrónico de la empresa, o 001-001).</summary>
    public string SerieRetencion { get; set; } = "001-001";

    /// <summary>Tipo de comprobante (<c>ShipVia</c>): FACTURA, NOTA DE VENTA, LIQUIDACION.</summary>
    public string ShipVia { get; set; } = string.Empty;

    /// <summary><c>ShipToZIP</c>: Manual / Externo / vacío.</summary>
    public string? Zip { get; set; }
}

/// <summary>Línea de la OC, en el orden en que se escribe.</summary>
public sealed class LineaOcContrato
{
    public decimal Cantidad { get; set; }

    /// <summary>Cuenta contable; vacía en las líneas «.» (Sage pone la del proveedor).</summary>
    public string? Cuenta { get; set; }

    public string Descripcion { get; set; } = string.Empty;

    /// <summary>ID del ítem de Sage; vacío en «.» y en la contrapartida de retención asumida.</summary>
    public string? Item { get; set; }

    public string? Job { get; set; }

    public decimal Monto { get; set; }

    public decimal PrecioUnitario { get; set; }

    /// <summary>Detalle, Impuesto, Propina, Autorizacion, Retencion, RetencionAsumida, Relleno.</summary>
    public string Tipo { get; set; } = string.Empty;
}

/// <summary>Proveedor tal como debe quedar en Sage (convenciones de <c>sageVendor</c>; port de <c>LoadVendor</c>).</summary>
public sealed class ProveedorContrato
{
    public string CuentaGasto { get; set; } = string.Empty;
    public string CustomField0 { get; set; } = string.Empty;
    public string CustomField1 { get; set; } = string.Empty;
    public string CustomField2 { get; set; } = string.Empty;
    public string CustomField3 { get; set; } = string.Empty;
    public string CustomField4 { get; set; } = string.Empty;
    public string Direccion1 { get; set; } = string.Empty;
    public string Direccion2 { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>Proveedor nuevo: se crea con este ID (si ya existe con otra identificación, el trabajo se rechaza).</summary>
    public bool EsNuevo { get; set; }

    public string Id { get; set; } = string.Empty;

    /// <summary>Identificación del SRI (<c>Address.Country</c>).</summary>
    public string Identificacion { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Telefono2 { get; set; } = string.Empty;

    /// <summary>Tipo de identificación (<c>AccountNumber</c> / <c>OurAccountWithThem</c>): 04, 05, 06, 08.</summary>
    public string TipoIdentificacion { get; set; } = string.Empty;
}

/// <summary>Resultado (JSON en <c>TrabajosSage.ResultadoJson</c>) de <see cref="TiposTrabajo.GuardarOc"/>.</summary>
public sealed class ResultadoGuardarOc
{
    /// <summary><c>Creada</c> o <c>Actualizada</c>.</summary>
    public string Accion { get; set; } = string.Empty;

    public string Mensaje { get; set; } = string.Empty;

    public string NumeroOc { get; set; } = string.Empty;

    public string NumeroRetencion { get; set; } = string.Empty;

    /// <summary><c>JrnlHdr.PostOrder</c> de la OC guardada (leído por ODBC).</summary>
    public int PostOrder { get; set; }

    /// <summary><c>Creado</c>, <c>Actualizado</c> o <c>SinCambios</c>.</summary>
    public string Proveedor { get; set; } = string.Empty;
}
