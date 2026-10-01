using System.Collections.Generic;

namespace PsaWeb.SageBridge.Contratos;

// Contratos de la Liquidación de Importaciones (PLAN-OLA2-LIQUIDACION-IMPORTACIONES §5). Propiedades en ORDEN ALFABÉTICO
// (ver GuardarOc.cs). Fechas como texto «yyyy-MM-dd».

/// <summary>
/// Payload de <see cref="TiposTrabajo.GuardarOcLiquidacion"/>: la OC de total 0 de una importación (port de
/// <c>sageImportMgm.loadingPOforImportsCost</c>). La cuenta de cada línea la pone el Bridge: la de inventario del ítem.
/// </summary>
public sealed class PayloadGuardarOcLiquidacion
{
    /// <summary>Cuenta de la importación (<c>IMPORTACION nn-aaaa</c>): cuenta por pagar de la OC y de la línea <c>LIQUIDACION</c>.</summary>
    public string CuentaImportacion { get; set; } = string.Empty;

    /// <summary>
    /// Cuenta por pagar de la OC (20000, decisión del usuario 2026-10-01; la compra la hereda). Vacía = la de la importación, como el
    /// `.exe`.
    /// </summary>
    public string CuentaPorPagar { get; set; } = string.Empty;

    /// <summary>Fecha de la OC (<c>yyyy-MM-dd</c>).</summary>
    public string Fecha { get; set; } = string.Empty;

    /// <summary>Un ítem de stock por línea; monto = valor + prorrateo.</summary>
    public List<LineaOcLiquidacion> Lineas { get; set; } = new List<LineaOcLiquidacion>();

    /// <summary>OC a actualizar en el lugar (la de <c>ImportCost.postOrderId</c>); nulo = crear.</summary>
    public int? PostOrder { get; set; }

    public string ProveedorId { get; set; } = string.Empty;

    /// <summary>Referencia de la OC: prefijo + «-» + número (<c>LIQ IMPORT-041-2026</c>).</summary>
    public string Referencia { get; set; } = string.Empty;
}

public sealed class LineaOcLiquidacion
{
    public decimal Cantidad { get; set; }

    public string Descripcion { get; set; } = string.Empty;

    public string Item { get; set; } = string.Empty;

    public decimal Monto { get; set; }
}

/// <summary>Resultado de <see cref="TiposTrabajo.GuardarOcLiquidacion"/>.</summary>
public sealed class ResultadoGuardarOcLiquidacion
{
    /// <summary><c>Creada</c> o <c>Actualizada</c>.</summary>
    public string Accion { get; set; } = string.Empty;

    /// <summary>GUID de la OC en Sage (<c>ImportCost.postOrderStrKey</c>).</summary>
    public string Clave { get; set; } = string.Empty;

    public string Mensaje { get; set; } = string.Empty;

    public int PostOrder { get; set; }

    public string Referencia { get; set; } = string.Empty;
}

/// <summary>Payload de <see cref="TiposTrabajo.ConvertirLiquidacion"/>.</summary>
public sealed class PayloadConvertirLiquidacion
{
    /// <summary>La OC de liquidación a convertir en compra.</summary>
    public int PostOrder { get; set; }
}

/// <summary>Resultado de <see cref="TiposTrabajo.ConvertirLiquidacion"/>.</summary>
public sealed class ResultadoConvertirLiquidacion
{
    public string Mensaje { get; set; } = string.Empty;

    public int PostOrderCompra { get; set; }

    public int PostOrderOc { get; set; }

    /// <summary>Referencia de la compra (<c>LIQ IMPORT 041-2026</c>).</summary>
    public string Referencia { get; set; } = string.Empty;

    /// <summary>La compra ya existía (reintento o registrada a mano): no se creó otra.</summary>
    public bool YaExistia { get; set; }
}

/// <summary>Convención de referencias de la liquidación (§1.6 del plan).</summary>
public static class ReferenciasLiquidacion
{
    /// <summary>La compra lleva la referencia de la OC con el primer guion cambiado por espacio (<c>LIQ IMPORT-041-2026</c> → <c>LIQ IMPORT 041-2026</c>).</summary>
    public static string DeCompra(string referenciaOc)
    {
        var i = referenciaOc.IndexOf('-');
        return i < 0 ? referenciaOc : referenciaOc.Substring(0, i) + " " + referenciaOc.Substring(i + 1);
    }
}
