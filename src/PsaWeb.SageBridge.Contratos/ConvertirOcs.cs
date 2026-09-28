using System.Collections.Generic;

namespace PsaWeb.SageBridge.Contratos;

// Contratos del trabajo ConvertirOcs (F6, reemplazo del worker COM PSComInvoiceGenerate). Propiedades en ORDEN ALFABÉTICO
// (ver GuardarOc.cs).

/// <summary>Payload opcional de <see cref="TiposTrabajo.ConvertirOcs"/>: sin <see cref="PostOrders"/> se convierten todas las pendientes.</summary>
public sealed class PayloadConvertirOcs
{
    /// <summary>Solo estas OC (PostOrder). Vacío o nulo = todas las pendientes de la empresa.</summary>
    public List<int>? PostOrders { get; set; }
}

/// <summary>Una OC convertida en compra.</summary>
public sealed class CompraConvertida
{
    /// <summary>Nº de factura (<c>ShipToAddress1</c> de la OC = <c>ReferenceNumber</c> de la compra).</summary>
    public string Factura { get; set; } = string.Empty;

    public int PostOrderCompra { get; set; }

    public int PostOrderOc { get; set; }

    public string ReferenciaOc { get; set; } = string.Empty;
}

/// <summary>Resultado (JSON en <c>TrabajosSage.ResultadoJson</c>) de <see cref="TiposTrabajo.ConvertirOcs"/>.</summary>
public sealed class ResultadoConvertirOcs
{
    public List<CompraConvertida> Convertidas { get; set; } = new List<CompraConvertida>();

    /// <summary>OC que no se pudieron convertir (con el motivo); se reintentan en la próxima corrida.</summary>
    public List<string> Errores { get; set; } = new List<string>();

    /// <summary>OC pendientes encontradas (antes de convertir).</summary>
    public int Pendientes { get; set; }

    /// <summary>OC que ya tenían compra y solo faltaba anotarlas en <c>PurchaseOrderSync</c>.</summary>
    public int SoloSincronizadas { get; set; }
}
