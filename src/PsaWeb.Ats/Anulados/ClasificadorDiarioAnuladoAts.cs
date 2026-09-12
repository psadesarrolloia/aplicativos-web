namespace PsaWeb.Ats.Anulados;

/// <summary>Port mínimo de <c>SageJournalMetaData.JrnlKeyJournalType</c>.</summary>
public enum DiarioAnuladoAts { Sales, PurchaseReceiveInventory, PurchaseOrder }

/// <summary>Port mínimo de <c>SageJournalMetaData.JournalExType</c>.</summary>
public enum SubtipoAnuladoAts { SaleInvoice, SaleCreditMemo, PurchaseReceiveInventory, PurchaseCreditMemo, PurchaseOrder }

/// <summary>
/// Clasifica los códigos crudos de Sage (<c>JrnlKey_Journal</c>/<c>JournalEx</c>)
/// a los diarios que usa <c>LoadCanceled</c>. Port de
/// <c>SageJournalMetaData.IntToJrnlKeyJournalType</c>/<c>IntToJournalExType</c>
/// (<c>Sage50MetaData</c>) — solo lo que necesita el ATS, no toda la librería.
/// </summary>
public static class ClasificadorDiarioAnuladoAts
{
    /// <remarks>
    /// Un valor no reconocido cae en <see cref="DiarioAnuladoAts.Sales"/> — así
    /// es el `.exe` (el <c>switch</c> de <c>IntToJrnlKeyJournalType</c> no tiene
    /// <c>default</c> propio, el valor inicial de la variable ya es Sales).
    /// </remarks>
    public static DiarioAnuladoAts ClasificarDiario(int valor) => valor switch
    {
        3 => DiarioAnuladoAts.Sales,
        4 => DiarioAnuladoAts.PurchaseReceiveInventory,
        10 => DiarioAnuladoAts.PurchaseOrder,
        _ => DiarioAnuladoAts.Sales,
    };

    /// <remarks>Un valor no reconocido cae en <see cref="SubtipoAnuladoAts.SaleInvoice"/>, misma razón que arriba.</remarks>
    public static SubtipoAnuladoAts ClasificarSubtipo(int valor) => valor switch
    {
        8 => SubtipoAnuladoAts.SaleInvoice,
        9 => SubtipoAnuladoAts.SaleCreditMemo,
        11 => SubtipoAnuladoAts.PurchaseReceiveInventory,
        12 => SubtipoAnuladoAts.PurchaseCreditMemo,
        18 => SubtipoAnuladoAts.PurchaseOrder,
        _ => SubtipoAnuladoAts.SaleInvoice,
    };
}
